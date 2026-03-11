using Binance.Net.Enums;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public sealed class SmartCooldownService
{
    private readonly CoinSoulDbContext _db;
    private readonly IMarketKlineProvider _klines;

    public SmartCooldownService(CoinSoulDbContext db, IMarketKlineProvider klines)
    {
        _db = db;
        _klines = klines;
    }

    /// <summary>
    /// Checks if symbol can enter based on cooldown rules
    /// </summary>
    public async Task<SmartCooldownDecision> CanEnterAsync(string symbol, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var settings = await _db.BotSettings.AsNoTracking().FirstAsync(ct);

        if (!settings.EnableSmartCooldown)
        {
            return new SmartCooldownDecision
            {
                Allowed = true,
                CooldownUntilUtc = null,
                Reason = "SmartCooldown disabled"
            };
        }

        var cooldown = await _db.TradeCooldowns
            .FirstOrDefaultAsync(c => c.Symbol == symbol, ct);

        if (cooldown == null)
        {
            cooldown = new TradeCooldownEntity
            {
                Symbol = symbol,
                WindowStartUtc = nowUtc,
                AttemptsInWindow = 0
            };
            _db.TradeCooldowns.Add(cooldown);
            await _db.SaveChangesAsync(ct);
        }

        if (cooldown.CooldownUntilUtc.HasValue && nowUtc < cooldown.CooldownUntilUtc.Value)
        {
            return new SmartCooldownDecision
            {
                Allowed = false,
                CooldownUntilUtc = cooldown.CooldownUntilUtc,
                Reason = "COOLDOWN_ACTIVE"
            };
        }

        if (cooldown.LastEntryUtc.HasValue)
        {
            var sinceEntry = nowUtc - cooldown.LastEntryUtc.Value;
            if (sinceEntry.TotalSeconds < settings.CooldownAfterEntrySeconds)
            {
                var cooldownUntil = cooldown.LastEntryUtc.Value.AddSeconds(settings.CooldownAfterEntrySeconds);
                return new SmartCooldownDecision
                {
                    Allowed = false,
                    CooldownUntilUtc = cooldownUntil,
                    Reason = "COOLDOWN_AFTER_ENTRY"
                };
            }
        }

        if (cooldown.LastLossUtc.HasValue)
        {
            var sinceLoss = nowUtc - cooldown.LastLossUtc.Value;
            if (sinceLoss.TotalSeconds < settings.CooldownAfterLossSeconds)
            {
                var cooldownUntil = cooldown.LastLossUtc.Value.AddSeconds(settings.CooldownAfterLossSeconds);
                return new SmartCooldownDecision
                {
                    Allowed = false,
                    CooldownUntilUtc = cooldownUntil,
                    Reason = "COOLDOWN_AFTER_LOSS"
                };
            }
        }

        var windowDuration = nowUtc - cooldown.WindowStartUtc;
        if (windowDuration.TotalMinutes >= 15)
        {
            cooldown.WindowStartUtc = nowUtc;
            cooldown.AttemptsInWindow = 0;
            await _db.SaveChangesAsync(ct);
        }

        if (cooldown.AttemptsInWindow >= settings.MaxEntryAttemptsPerSymbolPer15Min)
        {
            cooldown.CooldownUntilUtc = nowUtc.AddSeconds(settings.CooldownAfterTooManyAttemptsSeconds);
            await _db.SaveChangesAsync(ct);
            
            return new SmartCooldownDecision
            {
                Allowed = false,
                CooldownUntilUtc = cooldown.CooldownUntilUtc,
                Reason = "COOLDOWN_TOO_MANY_ATTEMPTS"
            };
        }

        return new SmartCooldownDecision
        {
            Allowed = true,
            CooldownUntilUtc = null,
            Reason = "OK"
        };
    }

    /// <summary>
    /// Checks if symbol is experiencing volatility spike
    /// </summary>
    public async Task<SmartCooldownDecision> CheckSpikeBlockAsync(string symbol, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var settings = await _db.BotSettings.AsNoTracking().FirstAsync(ct);

        if (!settings.EnableSpikeBlock)
        {
            return new SmartCooldownDecision
            {
                Allowed = true,
                CooldownUntilUtc = null,
                Reason = "SpikeBlock disabled"
            };
        }

        try
        {
            var lookback = settings.SpikeCheckLookbackMinutes;
            var closes = await _klines.GetClosesAsync(symbol, KlineInterval.OneMinute, lookback + 5, ct);
            
            if (closes == null || closes.Count < 5)
            {
                return new SmartCooldownDecision
                {
                    Allowed = true,
                    CooldownUntilUtc = null,
                    Reason = "Insufficient kline data"
                };
            }

            var arr = closes.ToArray();
            var lastClose = arr[^1];
            var lastOpen = arr.Length >= 2 ? arr[^2] : lastClose;

            var move1mPct = lastOpen > 0 ? Math.Abs((lastClose - lastOpen) / lastOpen) * 100m : 0;

            if (move1mPct > settings.SpikeBlock1mMovePct)
            {
                return new SmartCooldownDecision
                {
                    Allowed = false,
                    CooldownUntilUtc = nowUtc.AddMinutes(2),
                    Reason = $"SPIKE_1M_MOVE ({move1mPct:0.00}%)"
                };
            }

            if (arr.Length >= lookback)
            {
                var trueRanges = new List<decimal>();
                for (int i = 1; i < Math.Min(lookback, arr.Length); i++)
                {
                    var high = arr[i];
                    var low = arr[i];
                    var prevClose = arr[i - 1];
                    
                    var tr = Math.Max(
                        high - low,
                        Math.Max(
                            Math.Abs(high - prevClose),
                            Math.Abs(low - prevClose)
                        )
                    );
                    trueRanges.Add(tr);
                }

                var atr = trueRanges.Any() ? trueRanges.Average() : 0;
                var atrPct = lastClose > 0 ? (atr / lastClose) * 100m : 0;

                if (atrPct > settings.SpikeBlockAtrPct)
                {
                    return new SmartCooldownDecision
                    {
                        Allowed = false,
                        CooldownUntilUtc = nowUtc.AddMinutes(3),
                        Reason = $"SPIKE_ATR_TOO_HIGH ({atrPct:0.00}%)"
                    };
                }
            }

            return new SmartCooldownDecision
            {
                Allowed = true,
                CooldownUntilUtc = null,
                Reason = "OK"
            };
        }
        catch
        {
            return new SmartCooldownDecision
            {
                Allowed = true,
                CooldownUntilUtc = null,
                Reason = "Spike check failed - allow"
            };
        }
    }

    /// <summary>
    /// Records entry attempt (rejected or proceeding)
    /// </summary>
    public async Task RecordEntryAttemptAsync(string symbol, DateTimeOffset nowUtc, string reason, CancellationToken ct)
    {
        var cooldown = await _db.TradeCooldowns
            .FirstOrDefaultAsync(c => c.Symbol == symbol, ct);

        if (cooldown == null)
        {
            cooldown = new TradeCooldownEntity
            {
                Symbol = symbol,
                WindowStartUtc = nowUtc,
                AttemptsInWindow = 1,
                LastRejectionUtc = nowUtc,
                LastReason = reason
            };
            _db.TradeCooldowns.Add(cooldown);
        }
        else
        {
            cooldown.AttemptsInWindow++;
            cooldown.LastRejectionUtc = nowUtc;
            cooldown.LastReason = reason;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records successful entry fill
    /// </summary>
    public async Task RecordEntryFilledAsync(string symbol, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var cooldown = await _db.TradeCooldowns
            .FirstOrDefaultAsync(c => c.Symbol == symbol, ct);

        if (cooldown == null)
        {
            cooldown = new TradeCooldownEntity
            {
                Symbol = symbol,
                WindowStartUtc = nowUtc,
                AttemptsInWindow = 0,
                LastEntryUtc = nowUtc
            };
            _db.TradeCooldowns.Add(cooldown);
        }
        else
        {
            cooldown.LastEntryUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Records position closed at loss
    /// </summary>
    public async Task RecordLossClosedAsync(string symbol, DateTimeOffset nowUtc, CancellationToken ct)
    {
        var cooldown = await _db.TradeCooldowns
            .FirstOrDefaultAsync(c => c.Symbol == symbol, ct);

        if (cooldown == null)
        {
            cooldown = new TradeCooldownEntity
            {
                Symbol = symbol,
                WindowStartUtc = nowUtc,
                AttemptsInWindow = 0,
                LastLossUtc = nowUtc
            };
            _db.TradeCooldowns.Add(cooldown);
        }
        else
        {
            cooldown.LastLossUtc = nowUtc;
        }

        await _db.SaveChangesAsync(ct);
    }
}

public sealed class SmartCooldownDecision
{
    public bool Allowed { get; set; }
    public DateTimeOffset? CooldownUntilUtc { get; set; }
    public string Reason { get; set; } = "";
}