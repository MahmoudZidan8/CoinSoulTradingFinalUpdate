using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using CoinSoul.BinanceService.API;
using CoinSoul.BinanceService.Services.SpotTradeService;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine.Cache;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

public sealed class OpportunityDetector
{
    private static readonly string[] StableLikeSymbols = new[] { "USDCUSDT", "FDUSDUSDT", "USDEUSDT", "RLUSDUSDT", "TUSDUSDT", "EURUSDT", "AEURUSDT", "WBETHUSDT", "WETHUSDT", "USTCUSDT" };

    private readonly IMarketKlineProvider _klines;
    private readonly ISymbolValidator _validator;
    private readonly IBestSymbolsService _bestSymbols;
    private readonly CoinSoulDbContext _db;
    private readonly ILogger<OpportunityDetector> _logger;
    private readonly IBinanceRestClient _binance;
    private readonly IMarketDataCache _cache;
    private readonly IClock _clock;
    private readonly OpportunityScanOptions _scanOptions;
    private readonly decimal _tierAThreshold;
    private readonly decimal _tierBThreshold;
    private readonly decimal _tierCThreshold;
    private readonly decimal _expectedNetThresholdUsd;
    private readonly int _deepValidationTopN;
    private readonly decimal _apiBudgetPerMinute;
    
    private DateTime _lastScanUtc = DateTime.MinValue;

    public sealed record Candidate(string Symbol, decimal Score, string Reason);

    public sealed record ScanDiagnostics(
        int TotalScanned,
        int PrefilterPassed,
        int DeepAnalyzed,
        int FinalPassed,
        TimeSpan PrefilterDuration,
        TimeSpan DeepAnalyzeDuration,
        TimeSpan TotalDuration,
        Dictionary<string, int> RejectionCounts)
    {
        // ------------------------------------------------------------------
        // Compatibility helpers
        // Some callers (dashboard / orchestrator logs) expect these legacy
        // property names. Keep them as computed aliases.
        // ------------------------------------------------------------------
        public double TotalMs => TotalDuration.TotalMilliseconds;
        public int PrefilterCount => PrefilterPassed;
        public int DeepAnalysisCount => DeepAnalyzed;

        /// <summary>
        /// Short summary for log lines.
        /// - If FinalPassed &gt; 0: "FOUND"
        /// - If no rejection reasons: "NONE"
        /// - Else: most frequent rejection key
        /// </summary>
        public string Reason
        {
            get
            {
                if (FinalPassed > 0) return "FOUND";
                if (RejectionCounts is null || RejectionCounts.Count == 0) return "NONE";
                return RejectionCounts.OrderByDescending(kv => kv.Value).First().Key;
            }
        }
    }

    public OpportunityDetector(
        IMarketKlineProvider klines,
        ISymbolValidator validator,
        IBestSymbolsService bestSymbols,
        CoinSoulDbContext db,
        ILogger<OpportunityDetector> logger,
        IBinanceRestClient binance,
        IMarketDataCache cache,
        IClock clock,
        IConfiguration configuration)
    {
        _klines = klines;
        _validator = validator;
        _bestSymbols = bestSymbols;
        _db = db;
        _logger = logger;
        _binance = binance;
        _cache = cache;
        _clock = clock;
        
        _scanOptions = new OpportunityScanOptions();
        configuration.GetSection("OpportunityScan").Bind(_scanOptions);
        _tierAThreshold = configuration.GetValue<decimal>("OpportunityIntelligence:TierAConfidenceThreshold", 0.82m);
        _tierBThreshold = configuration.GetValue<decimal>("OpportunityIntelligence:TierBConfidenceThreshold", 0.68m);
        _tierCThreshold = configuration.GetValue<decimal>("OpportunityIntelligence:TierCConfidenceThreshold", 0.52m);
        _expectedNetThresholdUsd = configuration.GetValue<decimal>("OpportunityIntelligence:ExpectedNetAfterFeesUsd", 0.004m);
        _deepValidationTopN = configuration.GetValue<int>("OpportunityIntelligence:DeepValidationTopN", 20);
        _apiBudgetPerMinute = configuration.GetValue<decimal>("OpportunityIntelligence:ApiBudgetPerMinute", 900m);
    }

    /// <summary>
    /// Two-stage parallel opportunity scanning:
    /// 1. Fast prefilter using cached 24h tickers (volume + spread filter)
    /// 2. Parallel deep analysis of top candidates (book + klines + scoring)
    /// </summary>
    public async Task<(List<Candidate> Candidates, ScanDiagnostics Diagnostics)> ScanTopAsync(
        BotSettings settings,
        int takeTop = 8,
        int minScanSeconds = 5,
        CancellationToken ct = default)
    {
        var scanStart = _clock.UtcNow;
        
        // ✅ Throttle scanning to prevent spam
        if (_lastScanUtc.AddSeconds(minScanSeconds) > scanStart)
        {
            _logger.LogDebug("[SCAN_THROTTLED] Last scan was {Elapsed:F1}s ago (min={Min}s)",
                (scanStart - _lastScanUtc).TotalSeconds, minScanSeconds);
            return (new List<Candidate>(), CreateEmptyDiagnostics("THROTTLED"));
        }

        _lastScanUtc = scanStart;

        var rejectionCounts = InitializeRejectionCounts();

        // ====================================================================
        // STAGE 1: FAST PREFILTER using cached 24h tickers
        // ====================================================================
        var prefilterStart = _clock.UtcNow;
        
        // ✅ DIAGNOSTIC: Log prefilter start
        _logger.LogInformation("[DETECTOR_PREFILTER_START] Starting prefilter with MaxSpread={Spread}%, " +
            "MinVolume={Volume}, RsiMax={Rsi}, MomMin={Mom}%",
            settings.MaxSpreadPct, settings.MinVolume24hUsd, settings.RsiMaxForEntry, settings.MomentumMinPct);
        
        var prefilterCandidates = await PrefilterSymbolsAsync(settings, rejectionCounts, ct);
        var deepTake = settings.DeepScanTopN > 0 ? settings.DeepScanTopN : _deepValidationTopN;
        var deepSymbols = prefilterCandidates
            .Take(Math.Max(1, Math.Min(deepTake, 30)))
            .ToList();

        var prefilterDuration = _clock.UtcNow - prefilterStart;

        // ✅ DIAGNOSTIC: Log prefilter result
        _logger.LogCritical("[DETECTOR_PREFILTER_COMPLETE] Passed={Count}/{Total} in {Duration}ms | " +
            "Rejected: REGEX={Regex}, VOLUME_LOW={Vol}, VOLATILITY_HIGH={Volat}",
            deepSymbols.Count,
            rejectionCounts["TOTAL_SCANNED"],
            prefilterDuration.TotalMilliseconds,
            rejectionCounts["REGEX"],
            rejectionCounts["VOLUME_LOW"],
            rejectionCounts["VOLATILITY_HIGH"]);

        if (prefilterCandidates.Count == 0)
        {
            var scanTotalDuration = _clock.UtcNow - scanStart;
            var diag = new ScanDiagnostics(
                0, 0, 0, 0,
                prefilterDuration, TimeSpan.Zero, scanTotalDuration,
                rejectionCounts);
            
            LogScanSummary(diag, settings);
            return (new List<Candidate>(), diag);
        }

        // ====================================================================
        // STAGE 2: PARALLEL DEEP ANALYSIS with bounded concurrency
        // ====================================================================
        var deepAnalyzeStart = _clock.UtcNow;
        
        // ✅ DIAGNOSTIC: Log deep analyze start
        _logger.LogInformation("[DETECTOR_DEEP_START] Analyzing {Count} symbols with parallelism={Par}, timeout={Timeout}ms",
            deepSymbols.Count, _scanOptions.MaxParallelism, _scanOptions.DeepAnalyzeTimeoutMs);
        
        var deepResults = await DeepAnalyzeParallelAsync(
            deepSymbols, 
            settings, 
            rejectionCounts, 
            ct);
        
        var deepAnalyzeDuration = _clock.UtcNow - deepAnalyzeStart;

        // ✅ DIAGNOSTIC: Log deep analyze result
        _logger.LogCritical("[DETECTOR_DEEP_COMPLETE] Passed={Count}/{Total} in {Duration}ms | " +
            "Rejected: SPREAD={Spr}, RSI_HIGH={Rsi}, MOM_WEAK={Mom}, NO_BOOK={Book}, NO_KLINES={Kl}",
            deepResults.Count,
            deepSymbols.Count,
            deepAnalyzeDuration.TotalMilliseconds,
            rejectionCounts["SPREAD"],
            rejectionCounts["RSI_HIGH"],
            rejectionCounts["MOM_WEAK"],
            rejectionCounts["NO_BOOK"],
            rejectionCounts["NO_KLINES"]);

        // ====================================================================
        // STAGE 3: DETERMINISTIC ORDERING - Sort by Score desc, take top N
        // ====================================================================
        var finalCandidates = deepResults
            .OrderByDescending(c => c.Score)
            .Take(takeTop)
            .ToList();

        var totalDuration = _clock.UtcNow - scanStart;

        var diagnostics = new ScanDiagnostics(
            rejectionCounts["TOTAL_SCANNED"],
            deepSymbols.Count,
            deepResults.Count,
            finalCandidates.Count,
            prefilterDuration,
            deepAnalyzeDuration,
            totalDuration,
            rejectionCounts);

        LogScanSummary(diagnostics, settings);

        return (finalCandidates, diagnostics);
    }

    /// <summary>
    /// STAGE 1: Fast prefilter using cached 24h tickers
    /// Filters by: regex, volume, spread estimate
    /// Returns top N candidates for deep analysis
    /// </summary>
    private async Task<List<string>> PrefilterSymbolsAsync(
        BotSettings settings,
        Dictionary<string, int> rejectionCounts,
        CancellationToken ct)
    {
        var effectiveMode = settings.StrategyMode == StrategyMode.AutoScalperD
            ? StrategyAMode.Balanced
            : settings.StrategyAMode;

        // Get all 24h tickers from cache (very fast)
        var tickers = await _cache.GetOrFetch24hTickersAsync(ct);
        
        if (tickers == null || tickers.Count == 0)
        {
            rejectionCounts["NO_TICKERS"] = 1;
            _logger.LogWarning("[DETECTOR_PREFILTER_NO_TICKERS] Cache returned no tickers");
            return new List<string>();
        }

        _logger.LogInformation("[DETECTOR_PREFILTER_TICKERS] Retrieved {Count} tickers from cache",
            tickers.Count);

        var candidates = new List<(string Symbol, decimal Volume, decimal LastPrice)>();

        foreach (var ticker in tickers)
        {
            ct.ThrowIfCancellationRequested();
            
            rejectionCounts["TOTAL_SCANNED"]++;

            // ✅ Regex filter
            if (!System.Text.RegularExpressions.Regex.IsMatch(
                ticker.Symbol, @"^[A-Z0-9]{4,15}USDT$"))
            {
                rejectionCounts["REGEX"]++;
                continue;
            }

            if (StableLikeSymbols.Contains(ticker.Symbol, StringComparer.OrdinalIgnoreCase))
            {
                rejectionCounts["REGEX"]++;
                continue;
            }

            // ✅ Volume filter
            if (ticker.QuoteVolume < settings.MinVolume24hUsd)
            {
                rejectionCounts["VOLUME_LOW"]++;
                continue;
            }

            // ✅ Estimate spread from price change (rough filter)
            // Skip symbols with extreme volatility (likely wide spread)
            var absChange = Math.Abs(ticker.PriceChangePercent);
            if (absChange > 25m) // 25% in 24h = likely too volatile for this engine
            {
                rejectionCounts["VOLATILITY_HIGH"]++;
                continue;
            }

            candidates.Add((ticker.Symbol, ticker.QuoteVolume, ticker.LastPrice));
        }

        // ✅ Sort by volume descending, take top N for deep analysis
        var topCandidates = candidates
            .OrderByDescending(c => c.Volume)
                        .Take(Math.Max(250, _scanOptions.PrefilterTake))
            .Select(c => c.Symbol)
            .ToList();

        return topCandidates;
    }

    /// <summary>
    /// STAGE 2: Parallel deep analysis with bounded concurrency
    /// Fetches book ticker + klines for each candidate
    /// Computes RSI, momentum, spread, and score
    /// </summary>
    private async Task<List<Candidate>> DeepAnalyzeParallelAsync(
        List<string> symbols,
        BotSettings settings,
        Dictionary<string, int> rejectionCounts,
        CancellationToken ct)
    {
        var interval = KlineInterval.ThreeMinutes;
        var limit = 160;
        var rsiPeriod = 14;

        var parallelism = Math.Max(3, Math.Min(_scanOptions.MaxParallelism, 6));
        var semaphore = new SemaphoreSlim(parallelism);
        var results = new List<Candidate>();
        var resultsLock = new object();

        var tasks = symbols.Select(async symbol =>
        {
            await semaphore.WaitAsync(ct);
            
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(_scanOptions.DeepAnalyzeTimeoutMs);

                var candidate = await DeepAnalyzeSymbolAsync(
                    symbol, 
                    settings, 
                    interval, 
                    limit, 
                    rsiPeriod, 
                    rejectionCounts, 
                    cts.Token);

                if (candidate != null)
                {
                    lock (resultsLock)
                    {
                        results.Add(candidate);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                lock (rejectionCounts)
                {
                    rejectionCounts["TIMEOUT"]++;
                }
                
                _logger.LogWarning("[DEEP_TIMEOUT] {Symbol} exceeded {Timeout}ms",
                    symbol, _scanOptions.DeepAnalyzeTimeoutMs);
            }
            catch (Exception ex)
            {
                lock (rejectionCounts)
                {
                    rejectionCounts["EXCEPTION"]++;
                }
                
                _logger.LogError(ex, "[DEEP_ERROR] {Symbol}", symbol);
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);

        return results;
    }

    /// <summary>
    /// Deep analyze single symbol: book + klines + scoring
    /// ✅ ENHANCED: Logs each rejection reason
    /// </summary>
    private async Task<Candidate?> DeepAnalyzeSymbolAsync(
        string symbol,
        BotSettings settings,
        KlineInterval interval,
        int limit,
        int rsiPeriod,
        Dictionary<string, int> rejectionCounts,
        CancellationToken ct)
    {
        // ✅ Validator check
        if (!await _validator.ExistsAsync(symbol, ct))
        {
            lock (rejectionCounts) { rejectionCounts["VALIDATOR"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - VALIDATOR", symbol);
            return null;
        }

        // ✅ Fetch book ticker from cache
        var bookTicker = await _cache.GetOrFetchBookTickerAsync(symbol, ct);
        if (bookTicker == null)
        {
            lock (rejectionCounts) { rejectionCounts["NO_BOOK"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - NO_BOOK", symbol);
            return null;
        }

        if (bookTicker.BidPrice <= 0 || bookTicker.AskPrice <= 0)
        {
            lock (rejectionCounts) { rejectionCounts["BAD_BIDASK"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - BAD_BIDASK (Bid={Bid}, Ask={Ask})",
                symbol, bookTicker.BidPrice, bookTicker.AskPrice);
            return null;
        }

        // ✅ Spread check
        var spread = (bookTicker.AskPrice - bookTicker.BidPrice) / bookTicker.BidPrice * 100m;
        if (spread > settings.MaxSpreadPct)
        {
            lock (rejectionCounts) { rejectionCounts["SPREAD"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - SPREAD={Spread:F3}% > max={Max}%",
                symbol, spread, settings.MaxSpreadPct);
            return null;
        }

        // ✅ Fetch klines from cache
        var klineData = await _cache.GetOrFetchKlinesAsync(symbol, interval, limit, ct);
        if (klineData == null || klineData.Closes.Count < 50)
        {
            lock (rejectionCounts) { rejectionCounts["NO_KLINES"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - NO_KLINES (count={Count})",
                symbol, klineData?.Closes.Count ?? 0);
            return null;
        }

        var closes = klineData.Closes;
        var highs = klineData.Highs;
        var lows = klineData.Lows;

        // ✅ Calculate RSI
        var rsi = CalculateRsi(closes, rsiPeriod);
        if (rsi > settings.RsiMaxForEntry)
        {
            lock (rejectionCounts) { rejectionCounts["RSI_HIGH"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - RSI={Rsi:F1} > max={Max}",
                symbol, rsi, settings.RsiMaxForEntry);
            return null;
        }

        // Momentum windows tuned for scalping
        var mom3 = closes.Count > 3 ? ((closes[^1] - closes[^3]) / closes[^3]) * 100m : 0m;
        var mom5 = closes.Count > 5 ? ((closes[^1] - closes[^5]) / closes[^5]) * 100m : 0m;
        var mom12 = closes.Count > 12 ? ((closes[^1] - closes[^12]) / closes[^12]) * 100m : 0m;
        var blendedMom = (mom3 * 0.45m) + (mom5 * 0.35m) + (mom12 * 0.20m);

        if (blendedMom < settings.MomentumMinPct)
        {
            lock (rejectionCounts) { rejectionCounts["MOM_WEAK"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - MOM={Mom:F2}% < min={Min}%",
                symbol, blendedMom, settings.MomentumMinPct);
            return null;
        }

        // ATR-ish volatility efficiency
        var atrPct = CalculateAtrPct(highs, lows, closes, 14);
        var recentRangePct = closes.Count > 10 && closes[^10] > 0 ? Math.Abs((closes[^1] - closes[^10]) / closes[^10]) * 100m : 0m;
        var volumeSpike = atrPct > 0 ? Math.Min(Math.Max(recentRangePct / atrPct, 0.6m), 2.5m) : 1m;
        var orderBookImbalance = bookTicker.AskQuantity > 0
            ? bookTicker.BidQuantity / bookTicker.AskQuantity
            : 1m;
        var volatilityEfficiency = atrPct > 0 ? Math.Min(Math.Max(blendedMom / atrPct, -2m), 3m) : 0m;

        if (settings.RejectShortTermPeak && mom3 > 6.0m && volumeSpike < 1.03m)
        {
            lock (rejectionCounts) { rejectionCounts["VOLATILITY_HIGH"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - POSSIBLE_FAKE_PUMP mom3={Mom3:F2}% volSpike={VolSpike:F2}",
                symbol, mom3, volumeSpike);
            return null;
        }

        var score = CalculateScore(rsi, blendedMom, spread, volumeSpike, orderBookImbalance, atrPct, volatilityEfficiency);
        if (score <= 0)
        {
            lock (rejectionCounts) { rejectionCounts["SCORE_ZERO"]++; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - SCORE_ZERO", symbol);
            return null;
        }

        var confidence = CalculateConfidence(score);
        var expectedNetAfterFeesUsd = CalculateExpectedNetAfterFeesUsd(score, spread, atrPct, settings);
        if (expectedNetAfterFeesUsd < _expectedNetThresholdUsd)
        {
            lock (rejectionCounts) { rejectionCounts["EXPECTED_NET_LOW"] = rejectionCounts.TryGetValue("EXPECTED_NET_LOW", out var c) ? c + 1 : 1; }
            _logger.LogDebug("[FILTER_REJECT] {Symbol} - EXPECTED_NET={Expected:F4} < target={Target:F4}", symbol, expectedNetAfterFeesUsd, _expectedNetThresholdUsd);
            return null;
        }

        var tier = GetTier(confidence);
        var reason = $"{tier}|CONF={confidence:F2}|NET={expectedNetAfterFeesUsd:F4}|RSI={rsi:F1}|MOM={blendedMom:F2}%|ATR={atrPct:F2}%|VOLx={volumeSpike:F2}|IMB={orderBookImbalance:F2}|SPREAD={spread:F3}%";

        _logger.LogInformation("[FILTER_ACCEPT] {Symbol} - TIER={Tier}, CONF={Conf:F2}, NET={Net:F4}, RSI={Rsi:F1}, MOM={Mom:F2}%, ATR={Atr:F2}%, VOLx={VolSpike:F2}, IMB={Imb:F2}, SPREAD={Spread:F3}%, SCORE={Score:F1}",
            symbol, tier, confidence, expectedNetAfterFeesUsd, rsi, blendedMom, atrPct, volumeSpike, orderBookImbalance, spread, score);

        return new Candidate(symbol, score, reason);
    }

    public async Task<(bool Ok, string Why)> ConfirmEntryNowAsync(

        string symbol,
        BotSettings settings,
        CancellationToken ct)
    {
        var bookTicker = await _cache.GetOrFetchBookTickerAsync(symbol, ct);
        if (bookTicker == null)
            return (false, "NO_BOOK_TICKER");

        var spread = (bookTicker.AskPrice - bookTicker.BidPrice) / bookTicker.BidPrice * 100m;
        var maxFinalSpread = settings.FinalEntryMaxSpreadPct > 0 ? settings.FinalEntryMaxSpreadPct : settings.MaxSpreadPct;
        if (spread > maxFinalSpread)
            return (false, $"SPREAD={spread:F3}% > max={maxFinalSpread}%");

        var imbalance = bookTicker.AskQuantity > 0 ? bookTicker.BidQuantity / bookTicker.AskQuantity : 0m;
        if (imbalance < settings.FinalEntryMinOrderbookImbalance)
            return (false, $"IMBALANCE={imbalance:F2} < min={settings.FinalEntryMinOrderbookImbalance:F2}");

        var klineData = await _cache.GetOrFetchKlinesAsync(
            symbol, 
            KlineInterval.ThreeMinutes, 
            100, 
            ct);
        
        if (klineData == null || klineData.Closes.Count < 14)
            return (false, "INSUFFICIENT_KLINES");

        var rsi = CalculateRsi(klineData.Closes, 14);

        if (rsi > settings.RsiMaxForEntry)
            return (false, $"RSI={rsi:F1} > max={settings.RsiMaxForEntry}");

        var closes = klineData.Closes;
        var mom3 = closes.Count > 3 ? ((closes[^1] - closes[^3]) / closes[^3]) * 100m : 0m;
        if (mom3 < settings.FinalEntryMinMomentumPct)
            return (false, $"MOM3={mom3:F3}% < min={settings.FinalEntryMinMomentumPct:F3}%");

        return (true, $"RSI={rsi:F1} IMB={imbalance:F2} MOM3={mom3:F3}%");
    }

    private void LogScanSummary(
        ScanDiagnostics diag,
        BotSettings settings)
    {
        var topReasons = diag.RejectionCounts
            .Where(kvp => kvp.Value > 0 && kvp.Key != "TOTAL_SCANNED")
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => $"{kvp.Key}={kvp.Value}")
            .ToList();

        _logger.LogCritical(
            "[DETECTOR_SCAN_COMPLETE] Scanned={Total}, Prefilter={Pre}, DeepAnalyzed={Deep}, Final={Final} | " +
            "PrefilterMs={PreMs:F0}, DeepMs={DeepMs:F0}, TotalMs={TotalMs:F0} | " +
            "TopRejects=[{Reasons}] | MaxSpread={Spread}%, RsiMax={Rsi}, MomMin={Mom}%, MinVol={Vol}, Mode={Mode}",
            diag.TotalScanned,
            diag.PrefilterPassed,
            diag.DeepAnalyzed,
            diag.FinalPassed,
            diag.PrefilterDuration.TotalMilliseconds,
            diag.DeepAnalyzeDuration.TotalMilliseconds,
            diag.TotalDuration.TotalMilliseconds,
            string.Join(", ", topReasons),
            settings.MaxSpreadPct,
            settings.RsiMaxForEntry,
            settings.MomentumMinPct,
            settings.MinVolume24hUsd,
            settings.StrategyMode);
    }

    private Dictionary<string, int> InitializeRejectionCounts()
    {
        return new Dictionary<string, int>
        {
            ["TOTAL_SCANNED"] = 0,
            ["REGEX"] = 0,
            ["VOLUME_LOW"] = 0,
            ["VOLATILITY_HIGH"] = 0,
            ["NO_TICKERS"] = 0,
            ["VALIDATOR"] = 0,
            ["NO_BOOK"] = 0,
            ["BAD_BIDASK"] = 0,
            ["SPREAD"] = 0,
            ["NO_KLINES"] = 0,
            ["RSI_HIGH"] = 0,
            ["MOM_WEAK"] = 0,
            ["SCORE_ZERO"] = 0,
            ["TIMEOUT"] = 0,
            ["EXCEPTION"] = 0
        };
    }

    private ScanDiagnostics CreateEmptyDiagnostics(string reason)
    {
        var counts = InitializeRejectionCounts();
        counts[reason] = 1;
        
        return new ScanDiagnostics(
            0, 0, 0, 0,
            TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
            counts);
    }

    private decimal CalculateRsi(List<decimal> closes, int period)
    {
        if (closes.Count < period + 1) return 50m;

        var gains = new List<decimal>();
        var losses = new List<decimal>();

        for (int i = 1; i < closes.Count; i++)
        {
            var change = closes[i] - closes[i - 1];
            gains.Add(change > 0 ? change : 0);
            losses.Add(change < 0 ? -change : 0);
        }

        var avgGain = gains.TakeLast(period).Average();
        var avgLoss = losses.TakeLast(period).Average();

        if (avgLoss == 0) return 100m;

        var rs = avgGain / avgLoss;
        return 100m - (100m / (1m + rs));
    }

    private decimal CalculateConfidence(decimal score)
    {
        var normalized = Math.Clamp(score / 100m, 0m, 1m);
        return Math.Round(normalized, 4);
    }

    private decimal CalculateExpectedNetAfterFeesUsd(decimal score, decimal spreadPct, decimal atrPct, BotSettings settings)
    {
        var grossMovePct = Math.Max(0m, Math.Min((atrPct * 0.90m) + (score / 180m), 1.80m));
        var feePct = (settings.MakerFeeRate + settings.TakerFeeRate) * 100m;
        var effectivePct = Math.Max(0m, grossMovePct - feePct - (spreadPct * 0.75m) - (settings.MaxEntrySlippagePct * 0.50m));
        var baseUsd = settings.TargetUsdPerTrade > 0 ? settings.TargetUsdPerTrade : 18m;
        return Math.Round(baseUsd * (effectivePct / 100m), 4);
    }

    private string GetTier(decimal confidence)
    {
        if (confidence >= _tierAThreshold) return "A";
        if (confidence >= _tierBThreshold) return "B";
        if (confidence >= _tierCThreshold) return "C";
        return "Z";
    }

    private decimal CalculateScore(decimal rsi, decimal mom, decimal spread, decimal volumeSpike, decimal orderBookImbalance, decimal atrPct, decimal volatilityEfficiency)
    {
        var rsiScore = Math.Max(0m, (75m - rsi) / 75m) * 18m;
        var momScore = Math.Max(0m, Math.Min(mom, 2.5m) / 2.5m) * 24m;
        var spreadScore = spread <= 0.05m ? 18m : Math.Max(0m, (0.25m - spread) / 0.20m) * 18m;
        var volumeScore = Math.Max(0m, Math.Min(volumeSpike, 2.5m) / 2.5m) * 16m;
        var bookScore = Math.Max(0m, Math.Min(orderBookImbalance, 2.0m) / 2.0m) * 10m;
        var atrScore = (atrPct >= 0.20m && atrPct <= 1.80m) ? 8m : Math.Max(0m, 8m - Math.Abs(atrPct - 0.75m) * 6m);
        var efficiencyScore = Math.Max(0m, Math.Min(volatilityEfficiency, 1.5m) / 1.5m) * 6m;
        var liquidityPenalty = spread > 0.18m ? 6m : 0m;

        return Math.Round(Math.Max(0m, rsiScore + momScore + spreadScore + volumeScore + bookScore + atrScore + efficiencyScore - liquidityPenalty), 2);
    }

    private decimal CalculateAtrPct(List<decimal> highs, List<decimal> lows, List<decimal> closes, int period)
    {
        if (highs.Count < period + 1 || lows.Count < period + 1 || closes.Count < period + 1)
            return 0m;

        var trs = new List<decimal>();
        for (int i = 1; i < closes.Count; i++)
        {
            var high = highs[i];
            var low = lows[i];
            var prevClose = closes[i - 1];
            var tr = new[]
            {
                high - low,
                Math.Abs(high - prevClose),
                Math.Abs(low - prevClose)
            }.Max();
            trs.Add(tr);
        }

        var atr = trs.TakeLast(period).Average();
        var lastClose = closes[^1];
        return lastClose > 0 ? (atr / lastClose) * 100m : 0m;
    }
}

public sealed class OpportunityScanOptions
{
    public int MaxParallelism { get; set; } = 4;
    public int TakeTop { get; set; } = 20;
    public int PrefilterTake { get; set; } = 200;
    public int DeepAnalyzeTimeoutMs { get; set; } = 2000;
}
