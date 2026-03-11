using Binance.Net.Interfaces.Clients;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

public sealed class AutoScalperPositionManager
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<int, decimal> PeakGainPctByPosition = new();

    private readonly CoinSoulDbContext _db;
    private readonly ITradeExecutor _exec;
    private readonly ExecutionLockService _executionLock;
    private readonly SmartCooldownService _smartCooldown;
    private readonly IAccountTradeWriter _tradeWriter;
    private readonly IBinanceRestClient _binanceClient; // ✅ NEW for price fetch
    private readonly SymbolQueueManager _queueManager;
    private readonly ILogger<AutoScalperPositionManager> _logger; // ✅ NEW
    private const int MaxExitAttempts = 3;
    private static readonly TimeSpan ExitAttemptCooldown = TimeSpan.FromSeconds(60);

    public AutoScalperPositionManager(
        CoinSoulDbContext db,
        ITradeExecutor exec,
        ExecutionLockService executionLock,
        SmartCooldownService smartCooldown,
        IAccountTradeWriter tradeWriter,
        IBinanceRestClient binanceClient, // ✅ NEW
        SymbolQueueManager queueManager,
        ILogger<AutoScalperPositionManager> logger) // ✅ NEW
    {
        _db = db;
        _exec = exec;
        _executionLock = executionLock;
        _smartCooldown = smartCooldown;
        _tradeWriter = tradeWriter;
        _binanceClient = binanceClient; // ✅ NEW
        _queueManager = queueManager;
        _logger = logger; // ✅ NEW
    }

    public Task ManageAsync(CancellationToken ct) => ManageAsync(null, ct);

    public async Task ManageAsync(BotState? state, CancellationToken ct)
    {
        var openPositions = await _db.Positions
            .Where(p => p.IsOpen)
            .OrderBy(p => p.OpenedAtUtc)
            .ToListAsync(ct);

        if (!openPositions.Any())
            return;

        state?.AddLog("DEBUG", $"[POSITION_MGR] Managing {openPositions.Count} position(s)");

        var settings = await _db.BotSettings.AsNoTracking().OrderByDescending(x => x.Id).FirstOrDefaultAsync(ct);
        if (settings == null)
        {
            settings = new BotSettingsEntity();
            _db.BotSettings.Add(settings);
            await _db.SaveChangesAsync(ct);
        }

        foreach (var pos in openPositions)
        {
            await ManageSinglePositionAsync(pos, settings, state, ct);
        }
    }

    private async Task ManageSinglePositionAsync(
        PositionEntity pos,
        BotSettingsEntity settings,
        BotState? state,
        CancellationToken ct)
    {
        if (pos.ExitCompleted)
            return;

        // Skip if marked as dust
        if (pos.CloseReason == "DUST_IGNORED")
            return;

        if (pos.ExitRequested && pos.LastExitAttemptUtc != null)
        {
            var cooldown = DateTime.UtcNow - pos.LastExitAttemptUtc.Value;
            if (cooldown < ExitAttemptCooldown)
                return;
        }

        var age = DateTime.UtcNow - pos.OpenedAtUtc;

        var balance = await _exec.GetBaseAssetBalanceAsync(pos.Symbol, ct);
        var hasOpenOrders = await _exec.HasAnyOpenOrdersAsync(pos.Symbol, ct);

        if (!hasOpenOrders && balance.Total < (pos.Quantity * 0.20m))
        {
            state?.AddLog("INFO", $"[SYNC_CLOSE] {pos.Symbol}");

            pos.IsOpen = false;
            pos.IsActive = false;
            pos.ExitCompleted = true;
            pos.ClosedAtUtc = DateTime.UtcNow;
            pos.ExitReasonValue = ExitReason.ExternalClose.ToString();
            pos.UpdatedAtUtc = DateTime.UtcNow;

            await LogEventAsync("INFO", "SYNC_CLOSE", $"{pos.Symbol}: External close detected", pos.Id, pos.Symbol, ct);
            await _db.SaveChangesAsync(ct);
            return;
        }

        var maxDurationMinutes = settings.MaxTradeDurationMinutes > 0 ? settings.MaxTradeDurationMinutes : 30;

        // Smart trailing exit: once trade reaches meaningful profit, protect it on pullback
        var ticker = await _binanceClient.SpotApi.ExchangeData.GetTickerAsync(pos.Symbol, ct);
        var livePrice = ticker.Success && ticker.Data != null
            ? (ticker.Data.BestBidPrice > 0 ? ticker.Data.BestBidPrice : ticker.Data.LastPrice)
            : 0m;

        if (livePrice > 0 && pos.EntryPrice > 0)
        {
            var gainPct = ((livePrice - pos.EntryPrice) / pos.EntryPrice) * 100m;
            PeakGainPctByPosition.AddOrUpdate(pos.Id, gainPct, (_, oldPeak) => Math.Max(oldPeak, gainPct));
            var peakGain = PeakGainPctByPosition.TryGetValue(pos.Id, out var peak) ? peak : gainPct;

            var armPct = Math.Max(settings.TakeProfitGrossPct * 0.60m, 0.35m);
            var trailGapPct = 0.25m;
            if (peakGain >= armPct && gainPct <= peakGain - trailGapPct)
            {
                state?.AddLog("INFO", $"[TRAILING_EXIT] {pos.Symbol} live={gainPct:0.00}% peak={peakGain:0.00}%");
                await SafeProtectedExitAsync(pos, "SmartTrailingExit", settings, state, ct);
                PeakGainPctByPosition.TryRemove(pos.Id, out _);
                return;
            }
        }

        var review5 = age.TotalMinutes >= settings.SoftReviewMinutes1 && age.TotalMinutes < settings.SoftReviewMinutes2;
        var review15 = age.TotalMinutes >= settings.SoftReviewMinutes2 && age.TotalMinutes < settings.OpportunitySwitchHoldMinutes;
        var review30 = age.TotalMinutes >= settings.OpportunitySwitchHoldMinutes;

        if (review5)
        {
            state?.AddLog("INFO", $"[SOFT_REVIEW_5M] {pos.Symbol} age={age.TotalMinutes:0.0}m");
        }

        if (review15)
        {
            state?.AddLog("INFO", $"[SOFT_REVIEW_15M] {pos.Symbol} age={age.TotalMinutes:0.0}m");
        }

        if (review30)
        {
            var queueHead = _queueManager.Snapshot().FirstOrDefault();
            var currentGainPct = livePrice > 0 && pos.EntryPrice > 0 ? ((livePrice - pos.EntryPrice) / pos.EntryPrice) * 100m : 0m;
            var currentConfidence = Math.Clamp((currentGainPct + 1.5m) / 3m, 0m, 1m);
            var candidateConfidence = TryExtractConfidence(queueHead?.Reason);
            var confidenceGap = candidateConfidence - currentConfidence;
            var expectedSwitchNetUsd = settings.TargetUsdPerTrade * Math.Max(0m, confidenceGap) * 0.02m;
            var requiredSwitchNetUsd = settings.ExpectedNetAfterFeesUsd + (settings.TargetUsdPerTrade * (settings.TakerFeeRate + settings.MakerFeeRate));

            if (queueHead != null && !queueHead.Symbol.Equals(pos.Symbol, StringComparison.OrdinalIgnoreCase) &&
                candidateConfidence >= settings.TierAConfidenceThreshold &&
                confidenceGap >= settings.OpportunitySwitchMinConfidenceGap &&
                expectedSwitchNetUsd > requiredSwitchNetUsd)
            {
                state?.AddLog("WARN", $"[OPPORTUNITY_SWITCH] {pos.Symbol} -> {queueHead.Symbol} confGap={confidenceGap:0.00} expNet={expectedSwitchNetUsd:0.000}");
                await SafeProtectedExitAsync(pos, "OpportunitySwitch", settings, state, ct);
                PeakGainPctByPosition.TryRemove(pos.Id, out _);
                return;
            }

            state?.AddLog("WARN", $"[HOLD_REVIEW_30M] {pos.Symbol} no better switch found; continue holding");
        }
    }


    private static decimal TryExtractConfidence(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return 0m;
        var marker = "CONF=";
        var idx = reason.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return 0m;
        var start = idx + marker.Length;
        var end = reason.IndexOf('|', start);
        var slice = end > start ? reason[start..end] : reason[start..];
        return decimal.TryParse(slice, out var v) ? v : 0m;
    }

    private async Task SafeProtectedExitAsync(
        PositionEntity pos,
        string reason,
        BotSettingsEntity settings,
        BotState? state,
        CancellationToken ct)
    {
        if (pos.ExitCompleted)
            return;

        if (pos.ExitRequested && pos.LastExitAttemptUtc != null)
        {
            var cooldown = DateTime.UtcNow - pos.LastExitAttemptUtc.Value;
            if (cooldown < ExitAttemptCooldown)
                return;
        }

        // ✅ EXECUTION LOCK FOR EXIT
        await using (var lease = await _executionLock.TryAcquireEntryLockAsync(pos.Symbol, TimeSpan.FromSeconds(15), "AutoScalperExit", ct))
        {
            if (lease == null || !lease.Acquired)
            {
                state?.AddLog("DEBUG", $"[LOCK_BUSY] {pos.Symbol} EXIT");
                await LogEventAsync("DEBUG", "LOCK_BUSY", $"{pos.Symbol}: Exit lock held", pos.Id, pos.Symbol, ct);
                return;
            }

            try
            {
                pos.ExitRequested = true;
                pos.ExitAttempts++;
                pos.LastExitAttemptUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                await LogEventAsync("DEBUG", "EXIT_ATTEMPT", $"{pos.Symbol}: Attempt {pos.ExitAttempts}", pos.Id, pos.Symbol, ct);

                state?.AddLog("INFO", $"[EXIT_PREP] {pos.Symbol}");
                await _exec.CancelAllOpenOrdersAsync(pos.Symbol, ct);

                // Wait for order cancellation confirmation
                for (int i = 0; i < 5; i++)
                {
                    await Task.Delay(500, ct);
                    var hasOrders = await _exec.HasAnyOpenOrdersAsync(pos.Symbol, ct);
                    if (!hasOrders) break;
                    
                    if (i == 4)
                    {
                        state?.AddLog("ERROR", $"[EXIT_BLOCKED] {pos.Symbol} - orders still open");
                        await LogEventAsync("ERROR", "EXIT_BLOCKED", 
                            $"{pos.Symbol}: Orders still open after cancel attempts", pos.Id, pos.Symbol, ct);
                        return;
                    }
                }

                var rules = await _exec.GetRulesAsync(pos.Symbol, ct);
                if (rules == null)
                {
                    state?.AddLog("ERROR", $"[EXIT_FAIL] {pos.Symbol} - no rules");
                    await LogEventAsync("ERROR", "EXIT_FAIL", $"{pos.Symbol}: Rules unavailable", pos.Id, pos.Symbol, ct);
                    return;
                }

                var balance = await _exec.GetBaseAssetBalanceAsync(pos.Symbol, ct);
                var rawQty = Math.Min(balance.Total, pos.Quantity);
                var bufferedQty = ProfitTargetCalculator.ApplyQtyBuffer(rawQty, settings.QtyBufferPct);
                
                var qtyResult = TradingRulesNormalizer.NormalizeQuantity(bufferedQty, rules);
                var qtyToSell = qtyResult.NormalizedQty;

                if (!qtyResult.Valid)
                {
                    var hasOrders = await _exec.HasAnyOpenOrdersAsync(pos.Symbol, ct);
                    
                    if (!hasOrders && balance.Total == 0)
                    {
                        state?.AddLog("INFO", $"[SYNC_CLOSE] {pos.Symbol}");
                        pos.IsOpen = false;
                        pos.IsActive = false;
                        pos.ExitCompleted = true;
                        pos.ClosedAtUtc = DateTime.UtcNow;
                        pos.ExitReasonValue = ExitReason.ExternalClose.ToString();
                        pos.UpdatedAtUtc = DateTime.UtcNow;
                        await LogEventAsync("INFO", "SYNC_CLOSE", $"{pos.Symbol}: {qtyResult.Reason}", pos.Id, pos.Symbol, ct);
                        await _db.SaveChangesAsync(ct);
                        return;
                    }
                    else
                    {
                        state?.AddLog("ERROR", $"[SAFETY_EXIT_BLOCKED] {pos.Symbol}");
                        await LogEventAsync("ERROR", "SAFETY_EXIT_BLOCKED", $"{pos.Symbol}: {qtyResult.Reason}", pos.Id, pos.Symbol, ct);
                        return;
                    }
                }

                // ✅ CRITICAL: COMPREHENSIVE DUST HANDLING
                var dustCheck = await CheckAndHandleDustAsync(pos, qtyToSell, rules, settings, state, ct);
                if (dustCheck.IsDust)
                {
                    return; // Position safely closed as dust
                }

                var currentPrice = dustCheck.CurrentPrice;

                // Validate notional
                var notionalResult = TradingRulesNormalizer.ValidateNotional(currentPrice, qtyToSell, rules);
                if (!notionalResult.Valid)
                {
                    state?.AddLog("ERROR", $"[NOTIONAL_FAIL] {pos.Symbol}");
                    await LogEventAsync("ERROR", "EXIT_FAIL", $"{pos.Symbol}: {notionalResult.Reason}", pos.Id, pos.Symbol, ct);
                    return;
                }

                if (pos.ExitAttempts >= MaxExitAttempts)
                {
                    state?.AddLog("ERROR", $"[SAFETY_EXIT] {pos.Symbol}");
                    pos.IsOpen = false;
                    pos.IsActive = false;
                    pos.ExitCompleted = true;
                    pos.ClosedAtUtc = DateTime.UtcNow;
                    pos.ExitReasonValue = "SafetyExit";
                    pos.UpdatedAtUtc = DateTime.UtcNow;
                    await LogEventAsync("ERROR", "SAFETY_EXIT", $"{pos.Symbol}: Max attempts", pos.Id, pos.Symbol, ct);
                    await _db.SaveChangesAsync(ct);
                    return;
                }

                state?.AddLog("INFO", $"[EXIT_SELL] {pos.Symbol} - {qtyToSell:0.########}");
                var sell = await _exec.MarketSellAsync(pos.Symbol, qtyToSell, ct);

                if (!sell.Success)
                {
                    state?.AddLog("ERROR", $"[EXIT_FAIL] {pos.Symbol}: {sell.Error}");
                    await LogEventAsync("ERROR", "EXIT_FAIL", $"{pos.Symbol}: {sell.Error}", pos.Id, pos.Symbol, ct);
                    await _db.SaveChangesAsync(ct);
                    return;
                }

                pos.IsOpen = false;
                pos.IsActive = false;
                pos.ExitCompleted = true;
                pos.ClosedAtUtc = DateTime.UtcNow;
                pos.SellOrderId = sell.OrderId;
                pos.UpdatedAtUtc = DateTime.UtcNow;
                pos.ExitPrice = sell.AvgPrice;
                pos.ExitReasonValue = reason;

                var entryCost = pos.QuoteUsedUsdt;
                var exitValue = sell.AvgPrice * sell.ExecutedQty;
                var grossPnl = exitValue - entryCost;
                var totalFees = pos.FeesUsdt + (exitValue * settings.TakerFeeRate);
                pos.NetPnlUsdt = grossPnl - totalFees;

                if (pos.NetPnlUsdt < 0)
                {
                    await _smartCooldown.RecordLossClosedAsync(pos.Symbol, DateTimeOffset.UtcNow, ct);
                }

                state?.AddLog("TRADE", $"[EXIT] {pos.Symbol} PnL={pos.NetPnlUsdt:+0.00;-0.00}");
                await LogEventAsync("TRADE", "EXIT_OK", $"{pos.Symbol}: {reason}, pnl={pos.NetPnlUsdt:+0.00;-0.00}", pos.Id, pos.Symbol, ct);
                await _db.SaveChangesAsync(ct);

                await SaveSellTradeToAccountTradesAsync(pos.Symbol, sell, ct);
            }
            finally
            {
                await LogEventAsync("DEBUG", "LOCK_RELEASE", $"{pos.Symbol}: Exit lock released", pos.Id, pos.Symbol, ct);
            }
        }
    }

    /// <summary>
    /// Comprehensive dust detection and handling
    /// Fetches real-time price and validates against all thresholds
    /// </summary>
    private async Task<DustCheckResult> CheckAndHandleDustAsync(
        PositionEntity pos,
        decimal qtyToSell,
        SymbolTradingRules rules,
        BotSettingsEntity settings,
        BotState? state,
        CancellationToken ct)
    {
        // ✅ FETCH CURRENT MARKET PRICE
        decimal currentPrice = 0;
        try
        {
            var tickerResult = await _binanceClient.SpotApi.ExchangeData.GetTickerAsync(pos.Symbol, ct);
            if (tickerResult.Success && tickerResult.Data != null)
            {
                currentPrice = tickerResult.Data.BestBidPrice > 0 
                    ? tickerResult.Data.BestBidPrice 
                    : tickerResult.Data.LastPrice;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[DUST_CHECK_PRICE_ERROR] {Symbol} - using entry price", pos.Symbol);
            currentPrice = pos.EntryPrice; // Fallback to entry price
        }

        if (currentPrice <= 0)
        {
            _logger.LogWarning("[DUST_CHECK_PRICE_INVALID] {Symbol} - using entry price", pos.Symbol);
            currentPrice = pos.EntryPrice;
        }

        var usdValue = qtyToSell * currentPrice;

        _logger.LogDebug("[DUST_CHECK] {Symbol} qty={Qty} price={Price} value=${Value:N2} threshold=${Threshold:N2}",
            pos.Symbol, qtyToSell, currentPrice, usdValue, settings.DustIgnoreUsdThreshold);

        // ✅ CHECK 1: USD Value below threshold
        if (usdValue < settings.DustIgnoreUsdThreshold)
        {
            await HandleDustPositionAsync(pos, qtyToSell, currentPrice, usdValue, rules, settings, state, 
                $"USD value ${usdValue:N4} < threshold ${settings.DustIgnoreUsdThreshold:N2}", ct);
            
            return new DustCheckResult { IsDust = true, CurrentPrice = currentPrice };
        }

        // ✅ CHECK 2: Quantity below MinQty
        if (qtyToSell < rules.MinQty)
        {
            await HandleDustPositionAsync(pos, qtyToSell, currentPrice, usdValue, rules, settings, state,
                $"Qty {qtyToSell:0.########} < MinQty {rules.MinQty:0.########}", ct);
            
            return new DustCheckResult { IsDust = true, CurrentPrice = currentPrice };
        }

        // ✅ CHECK 3: Notional below MinNotional
        if (usdValue < rules.MinNotional)
        {
            await HandleDustPositionAsync(pos, qtyToSell, currentPrice, usdValue, rules, settings, state,
                $"Notional ${usdValue:N4} < MinNotional ${rules.MinNotional:N2}", ct);
            
            return new DustCheckResult { IsDust = true, CurrentPrice = currentPrice };
        }

        // Not dust - proceed with sell
        return new DustCheckResult { IsDust = false, CurrentPrice = currentPrice };
    }

    /// <summary>
    /// Safely marks position as dust and closes it without selling
    /// /// </summary>
    private async Task HandleDustPositionAsync(
        PositionEntity pos,
        decimal qty,
        decimal price,
        decimal usdValue,
        SymbolTradingRules rules,
        BotSettingsEntity settings,
        BotState? state,
        string reason,
        CancellationToken ct)
    {
        _logger.LogWarning("[DUST_SKIP] {Symbol} - {Reason}", pos.Symbol, reason);

        state?.AddLog("WARN", $"[DUST_IGNORED] {pos.Symbol} ${usdValue:N4} - {reason}");

        // ✅ SAFELY CLOSE POSITION WITHOUT SELLING
        pos.IsOpen = false;
        pos.IsActive = false;
        pos.ExitCompleted = true;
        pos.ClosedAtUtc = DateTime.UtcNow;
        pos.CloseReason = "DUST_IGNORED";
        pos.Stage = (int)TradeStage.Closed;

        // Don't set ExitPrice/SellOrderId since no sell occurred
        // Mark as small loss (dust value)
        pos.NetPnlUsdt = -(pos.QuoteUsedUsdt); // Lost the original investment (dust left)

        await _db.SaveChangesAsync(ct);

        // ✅ LOG COMPREHENSIVE DUST EVENT
        await LogDustEventAsync(pos, qty, price, usdValue, rules, reason, ct);

        // ✅ LOG TO Events table
        await LogEventAsync("WARN", "DUST_SKIP", 
            $"{pos.Symbol}: {reason} (qty={qty:0.########}, value=${usdValue:N4}, minQty={rules.MinQty:0.########}, minNotional=${rules.MinNotional:N2})",
            pos.Id, pos.Symbol, ct);
    }

    /// <summary>
    /// Logs detailed dust event to TradingEvents table
    /// </summary>
    private async Task LogDustEventAsync(
        PositionEntity pos,
        decimal qty,
        decimal price,
        decimal usdValue,
        SymbolTradingRules rules,
        string reason,
        CancellationToken ct)
    {
        try
        {
            _db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = "WARN",
                Type = "DUST_SKIP",
                Symbol = pos.Symbol,
                Message = reason,
                Price = price,
                Quantity = qty,
                RealizedPnlUsdt = pos.NetPnlUsdt,
                CorrelationId = pos.Id.ToString()
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DUST_LOG_ERROR] {Symbol}", pos.Symbol);
        }
    }

    private async Task SaveSellTradeToAccountTradesAsync(
        string symbol,
        LiveSellResult sellResult,
        CancellationToken ct)
    {
        try
        {
            var tradeId = GenerateTradeId(sellResult.OrderId ?? 0, DateTime.UtcNow);

            var accountTrade = new AccountTradeEntity
            {
                TradeId = tradeId,
                Symbol = symbol,
                Side = "SELL",
                Price = sellResult.AvgPrice,
                Quantity = sellResult.ExecutedQty,
                QuoteQty = sellResult.QuoteReceived,
                Commission = 0m,
                CommissionAsset = "UNKNOWN",
                IsMaker = false,
                TradeTimeUtc = DateTime.UtcNow,
                Source = "BOT",
                OrderId = sellResult.OrderId
            };

            await _tradeWriter.SaveAsync(accountTrade, ct);
        }
        catch (Exception ex)
        {
            await LogEventAsync("ERROR", "ACCOUNT_TRADE_SAVE_ERROR", 
                $"{symbol}: Failed to save SELL trade: {ex.Message}", 0, symbol, ct);
        }
    }

    private static long GenerateTradeId(long orderId, DateTime timestamp)
    {
        var timestampPart = (timestamp.Ticks / 10000000) & 0x7FF;
        return (orderId << 11) | timestampPart;
    }

    private async Task LogEventAsync(string level, string type, string message, int? positionId, string? symbol, CancellationToken ct)
    {
        _db.Events.Add(new EventEntity
        {
            Level = level,
            Type = type,
            Message = message,
            PositionId = positionId,
            Symbol = symbol,
            AtUtc = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(ct);
    }

    private async Task LogSafetyEventAsync(string type, string? symbol, string message, CancellationToken ct)
    {
        try
        {
            _db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = type == "RISK_STOP" ? "CRITICAL" : "WARN",
                Type = type,
                Symbol = symbol,
                Message = message
            });
            await _db.SaveChangesAsync(ct);
        }
        catch { }
    }
}

/// <summary>
/// Result of dust check operation
/// </summary>
internal sealed record DustCheckResult
{
    public bool IsDust { get; init; }
    public decimal CurrentPrice { get; init; }
}
