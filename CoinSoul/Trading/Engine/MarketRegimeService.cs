using Binance.Net.Enums;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public sealed class MarketRegimeService
{
    private readonly CoinSoulDbContext _db;
    private readonly IMarketKlineProvider _klines;
    
    private MarketRegimeDecision? _cachedDecision;
    private DateTimeOffset _cacheExpiry = DateTimeOffset.MinValue;
    private readonly TimeSpan _cacheDuration = TimeSpan.FromSeconds(45);
    private readonly object _cacheLock = new();

    public MarketRegimeService(CoinSoulDbContext db, IMarketKlineProvider klines)
    {
        _db = db;
        _klines = klines;
    }

    public async Task<MarketRegimeDecision> GetDecisionAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        lock (_cacheLock)
        {
            if (_cachedDecision != null && nowUtc < _cacheExpiry)
            {
                return _cachedDecision;
            }
        }

        var settings = await _db.BotSettings.AsNoTracking().FirstAsync(ct);

        if (!settings.EnableMarketRegimeFilter)
        {
            var decision = new MarketRegimeDecision
            {
                AllowedToTrade = true,
                Regime = MarketRegime.Unknown,
                RiskMultiplier = 1.00m,
                TpMultiplier = 1.00m,
                Reason = "RegimeFilter disabled",
                AsOfUtc = nowUtc
            };

            CacheDecision(decision, nowUtc);
            return decision;
        }

        try
        {
            var timeframe = ParseTimeframe(settings.RegimeTimeframe);
            var closes = await _klines.GetClosesAsync(
                settings.RegimeAnchorSymbol,
                timeframe,
                settings.RegimeLookbackBars,
                ct);

            if (closes == null || closes.Count < settings.RegimeSlowEmaPeriod + 10)
            {
                var failDecision = new MarketRegimeDecision
                {
                    AllowedToTrade = false,
                    Regime = MarketRegime.Unknown,
                    RiskMultiplier = 0,
                    TpMultiplier = 1.00m,
                    Reason = "REGIME_DATA_UNAVAILABLE",
                    AsOfUtc = nowUtc
                };

                CacheDecision(failDecision, nowUtc);
                return failDecision;
            }

            var arr = closes.ToArray();
            var lastClose = arr[^1];

            var ema50 = ComputeEma(arr, settings.RegimeFastEmaPeriod);
            var ema200 = ComputeEma(arr, settings.RegimeSlowEmaPeriod);

            var atrPct = ComputeAtrPercent(arr, settings.RegimeAtrPeriod, lastClose);

            var crash1hMovePct = ComputeCrashMove(arr, settings.CrashLookbackMinutes, timeframe);

            var regime = MarketRegime.Unknown;
            var reason = "";

            if (crash1hMovePct >= settings.Crash1hMovePct)
            {
                regime = MarketRegime.Crash;
                reason = $"CRASH_1H_MOVE ({crash1hMovePct:0.00}%)";
            }
            else if (atrPct < settings.SidewaysAtrPctThreshold)
            {
                regime = MarketRegime.Sideways;
                reason = $"SIDEWAYS_ATR_LOW ({atrPct:0.00}%)";
            }
            else if (ema50 > ema200)
            {
                regime = MarketRegime.BullTrend;
                reason = $"BULL_EMA50_GT_EMA200 (ATR {atrPct:0.00}%)";
            }
            else if (ema50 < ema200)
            {
                regime = MarketRegime.BearTrend;
                reason = $"BEAR_EMA50_LT_EMA200 (ATR {atrPct:0.00}%)";
            }
            else
            {
                regime = MarketRegime.Unknown;
                reason = "EMA_NEUTRAL";
            }

            var allowed = true;
            var riskMult = 1.00m;
            var tpMult = 1.00m;

            switch (regime)
            {
                case MarketRegime.BullTrend:
                    riskMult = settings.RiskMultBull;
                    tpMult = settings.TpMultBull;
                    break;

                case MarketRegime.BearTrend:
                    riskMult = settings.RiskMultBear;
                    tpMult = settings.TpMultBear;
                    break;

                case MarketRegime.Sideways:
                    riskMult = settings.RiskMultSideways;
                    tpMult = settings.TpMultSideways;
                    break;

                case MarketRegime.Crash:
                    riskMult = settings.RiskMultCrash;
                    tpMult = settings.TpMultCrash;
                    if (settings.BlockTradingOnCrash || riskMult <= 0)
                    {
                        allowed = false;
                    }
                    break;

                default:
                    riskMult = 1.00m;
                    tpMult = 1.00m;
                    break;
            }

            var finalDecision = new MarketRegimeDecision
            {
                AllowedToTrade = allowed,
                Regime = regime,
                RiskMultiplier = riskMult,
                TpMultiplier = tpMult,
                Reason = reason,
                AsOfUtc = nowUtc
            };

            CacheDecision(finalDecision, nowUtc);
            return finalDecision;
        }
        catch (Exception ex)
        {
            var errorDecision = new MarketRegimeDecision
            {
                AllowedToTrade = false,
                Regime = MarketRegime.Unknown,
                RiskMultiplier = 0,
                TpMultiplier = 1.00m,
                Reason = $"REGIME_ERROR: {ex.Message}",
                AsOfUtc = nowUtc
            };

            CacheDecision(errorDecision, nowUtc);
            return errorDecision;
        }
    }

    private void CacheDecision(MarketRegimeDecision decision, DateTimeOffset nowUtc)
    {
        lock (_cacheLock)
        {
            _cachedDecision = decision;
            _cacheExpiry = nowUtc.Add(_cacheDuration);
        }
    }

    private KlineInterval ParseTimeframe(string timeframe)
    {
        return timeframe.ToLowerInvariant() switch
        {
            "1m" => KlineInterval.OneMinute,
            "5m" => KlineInterval.FiveMinutes,
            "15m" => KlineInterval.FifteenMinutes,
            "1h" => KlineInterval.OneHour,
            _ => KlineInterval.FifteenMinutes
        };
    }

    private decimal ComputeEma(decimal[] closes, int period)
    {
        if (closes.Length < period)
            return closes[^1];

        var multiplier = 2m / (period + 1);
        var ema = closes.Take(period).Average();

        for (int i = period; i < closes.Length; i++)
        {
            ema = (closes[i] * multiplier) + (ema * (1 - multiplier));
        }

        return ema;
    }

    private decimal ComputeAtrPercent(decimal[] closes, int period, decimal lastClose)
    {
        if (closes.Length < period + 1 || lastClose <= 0)
            return 0;

        var trueRanges = new List<decimal>();

        for (int i = 1; i < Math.Min(period + 10, closes.Length); i++)
        {
            var high = closes[i];
            var low = closes[i];
            var prevClose = closes[i - 1];

            var tr = Math.Max(
                high - low,
                Math.Max(
                    Math.Abs(high - prevClose),
                    Math.Abs(low - prevClose)
                )
            );

            trueRanges.Add(tr);
        }

        if (!trueRanges.Any())
            return 0;

        var atr = trueRanges.Take(period).Average();
        return (atr / lastClose) * 100m;
    }

    private decimal ComputeCrashMove(decimal[] closes, int lookbackMinutes, KlineInterval timeframe)
    {
        if (closes.Length < 2)
            return 0;

        var barsPerHour = timeframe switch
        {
            KlineInterval.OneMinute => 60,
            KlineInterval.FiveMinutes => 12,
            KlineInterval.FifteenMinutes => 4,
            KlineInterval.OneHour => 1,
            _ => 4
        };

        var barsBack = Math.Min(barsPerHour, closes.Length - 1);
        var lastClose = closes[^1];
        var closeNAgo = closes[^(barsBack + 1)];

        if (closeNAgo <= 0)
            return 0;

        return Math.Abs((lastClose - closeNAgo) / closeNAgo) * 100m;
    }
}