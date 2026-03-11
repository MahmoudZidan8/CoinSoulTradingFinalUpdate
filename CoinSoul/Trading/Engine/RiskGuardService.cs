using CoinSoul.Entities;
using CoinSoul.Trading.Core;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public enum RiskPauseLevel
{
    None = 0,
    Pause30Min = 1,
    Pause3Hours = 2,
    StopUntilMidnight = 3
}

public sealed class RiskGuardService
{
    private readonly CoinSoulDbContext _db;

    private static RiskPauseLevel _currentLevel = RiskPauseLevel.None;
    private static DateTime? _lastTransitionUtc;
    private static bool _baselineWarningLogged = false;

    public RiskGuardService(CoinSoulDbContext db)
    {
        _db = db;
    }

    public async Task<(bool canTrade, string reason)> CanTradeNowAsync(CancellationToken ct = default)
    {
        var settings = await _db.BotSettings.FirstAsync(ct);
        var now = DateTime.UtcNow;

        if (settings.StopUntilUtc != null && now >= settings.StopUntilUtc.Value)
        {
            settings.StopUntilUtc = null;
            _currentLevel = RiskPauseLevel.None;
            await _db.SaveChangesAsync(ct);
            await LogRiskEventAsync("RISK_AUTO_CLEAR", "Stop expired", ct);
        }

        if (settings.PauseUntilUtc != null && now >= settings.PauseUntilUtc.Value)
        {
            settings.PauseUntilUtc = null;
            _currentLevel = RiskPauseLevel.None;
            await _db.SaveChangesAsync(ct);
            await LogRiskEventAsync("RISK_AUTO_CLEAR", "Pause expired", ct);
        }

        if (settings.StopUntilUtc != null && now < settings.StopUntilUtc.Value)
        {
            return (false, $"Stopped until {settings.StopUntilUtc.Value:yyyy-MM-dd HH:mm} UTC");
        }

        if (settings.PauseUntilUtc != null && now < settings.PauseUntilUtc.Value)
        {
            // ✅ Production safety: if baseline isn't initialized, a stale pause can deadlock the bot forever.
            // We auto-clear the pause and allow trading until EquityStartOfDayUsdt is set.
            if (settings.EquityStartOfDayUsdt <= 0)
            {
                var until = settings.PauseUntilUtc.Value;
                settings.PauseUntilUtc = null;
                _currentLevel = RiskPauseLevel.None;
                await _db.SaveChangesAsync(ct);
                await LogRiskEventAsync("RISK_PAUSE_IGNORED", $"Pause cleared (baseline missing). Previous PauseUntilUtc={until:O}", ct);
            }
            else
            {
                return (false, $"Paused until {settings.PauseUntilUtc.Value:yyyy-MM-dd HH:mm} UTC");
            }
        }

        if (await IsNewDayAsync(ct))
        {
            await ResetDailyEquityAsync(ct);
            _currentLevel = RiskPauseLevel.None;
            _lastTransitionUtc = null;
            _baselineWarningLogged = false;
        }

        if (settings.EquityStartOfDayUsdt <= 0)
        {
            // Try to initialize baseline from latest equity snapshot to keep risk logic consistent.
            var latestSnapshot = await _db.EquitySnapshotEntity
                .OrderByDescending(s => s.AtUtc)
                .FirstOrDefaultAsync(ct);

            if (latestSnapshot != null)
            {
                settings.EquityStartOfDayUsdt = latestSnapshot.TotalEquityUsdt;
                await _db.SaveChangesAsync(ct);
                await LogRiskEventAsync("RISK_BASELINE_INIT", $"Baseline initialized from snapshot - ${latestSnapshot.TotalEquityUsdt:N2}", ct);
                _baselineWarningLogged = false;
                // continue (do not early-return) so risk checks run using the baseline we just set
            }
            else
            {
                if (!_baselineWarningLogged)
                {
                    await LogRiskEventAsync("RISK_BASELINE_WARN", "StartOfDayEquity not set and no snapshots exist yet - trading allowed", ct);
                    _baselineWarningLogged = true;
                }
                return (true, "StartOfDayEquity not initialized - trading allowed");
            }
        }

        var pnlPct = await GetDailyPnLPercentAsync(ct);
        await ApplyRiskGuardsAsync(pnlPct, settings, ct);

        return (true, $"Daily P&L: {pnlPct:+0.00;-0.00}%");
    }

    public async Task<CanEnterResult> CanEnterNewTradeAsync(CancellationToken ct = default)
    {
        var result = await CanTradeNowAsync(ct);
        var state = await GetRiskStateAsync(ct);

        return new CanEnterResult
        {
            CanEnter = result.canTrade,
            Reason = result.reason,
            State = state
        };
    }

    public async Task<decimal> GetDailyPnLPercentAsync(CancellationToken ct = default)
    {
        var settings = await _db.BotSettings.AsNoTracking().FirstAsync(ct);

        if (settings.EquityStartOfDayUsdt <= 0)
            return 0m;

        var latestSnapshot = await _db.EquitySnapshotEntity
            .OrderByDescending(s => s.AtUtc)
            .FirstOrDefaultAsync(ct);

        var currentEquity = latestSnapshot?.TotalEquityUsdt ?? settings.EquityStartOfDayUsdt;

        if (currentEquity <= 0)
            return 0m;

        return ((currentEquity - settings.EquityStartOfDayUsdt) / settings.EquityStartOfDayUsdt) * 100m;
    }

    public async Task<RiskStateDto> GetRiskStateAsync(CancellationToken ct = default)
    {
        var settings = await _db.BotSettings.AsNoTracking().FirstAsync(ct);
        var pnlPct = await GetDailyPnLPercentAsync(ct);

        var latestSnapshot = await _db.EquitySnapshotEntity
            .OrderByDescending(s => s.AtUtc)
            .FirstOrDefaultAsync(ct);

        var currentEquity = latestSnapshot?.TotalEquityUsdt ?? settings.EquityStartOfDayUsdt;
        var now = DateTime.UtcNow;

        var status = "SAFE";
        var color = "green";
        var message = $"Drawdown: {pnlPct:+0.00;-0.00}%";

        if (settings.StopUntilUtc != null && now < settings.StopUntilUtc.Value)
        {
            status = "STOPPED";
            color = "red";
            message = $"Stopped until {settings.StopUntilUtc.Value:yyyy-MM-dd HH:mm} UTC";
        }
        else if (settings.PauseUntilUtc != null && now < settings.PauseUntilUtc.Value)
        {
            status = "PAUSED";
            color = "orange";
            message = $"Paused until {settings.PauseUntilUtc.Value:yyyy-MM-dd HH:mm} UTC";
        }
        else if (pnlPct <= -5m)
        {
            status = "WARNING";
            color = "orange";
        }

        return new RiskStateDto
        {
            Status = status,
            StatusColor = color,
            CurrentEquityUsdt = currentEquity,
            StartOfDayEquityUsdt = settings.EquityStartOfDayUsdt,
            DrawdownPct = pnlPct,
            PauseUntilUtc = settings.PauseUntilUtc,
            StopUntilUtc = settings.StopUntilUtc,
            Message = message
        };
    }

    private async Task ApplyRiskGuardsAsync(decimal pnlPct, BotSettingsEntity settings, CancellationToken ct)
    {
        if (pnlPct <= settings.RiskGuardStopUntilMidnightPct)
        {
            var nextMidnight = DateTime.UtcNow.Date.AddDays(1);

            if (_currentLevel != RiskPauseLevel.StopUntilMidnight)
            {
                _currentLevel = RiskPauseLevel.StopUntilMidnight;
                settings.StopUntilUtc = nextMidnight;
                settings.PauseUntilUtc = null;
                await _db.SaveChangesAsync(ct);
                await LogRiskEventAsync("RISK_STOP", $"Loss {pnlPct:0.00}%", ct);
            }
            return;
        }

        if (pnlPct <= settings.RiskGuardPause3HourPct)
        {
            if (_currentLevel != RiskPauseLevel.Pause3Hours)
            {
                _currentLevel = RiskPauseLevel.Pause3Hours;
                settings.PauseUntilUtc = DateTime.UtcNow.AddHours(3);
                settings.StopUntilUtc = null;
                await _db.SaveChangesAsync(ct);
                await LogRiskEventAsync("RISK_PAUSE_3H", $"Loss {pnlPct:0.00}%", ct);
            }
            return;
        }

        if (pnlPct <= settings.RiskGuardPause30MinPct)
        {
            if (_currentLevel != RiskPauseLevel.Pause30Min)
            {
                _currentLevel = RiskPauseLevel.Pause30Min;
                settings.PauseUntilUtc = DateTime.UtcNow.AddMinutes(30);
                settings.StopUntilUtc = null;
                await _db.SaveChangesAsync(ct);
                await LogRiskEventAsync("RISK_PAUSE_30M", $"Loss {pnlPct:0.00}%", ct);
            }
            return;
        }

        if (_currentLevel != RiskPauseLevel.None && pnlPct > settings.RiskGuardPause30MinPct)
        {
            _currentLevel = RiskPauseLevel.None;
            settings.PauseUntilUtc = null;
            settings.StopUntilUtc = null;
            await _db.SaveChangesAsync(ct);
            await LogRiskEventAsync("RISK_CLEAR", $"Recovered {pnlPct:0.00}%", ct);
        }
    }

    private async Task<bool> IsNewDayAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var lastSnapshot = await _db.EquitySnapshotEntity
            .OrderByDescending(s => s.AtUtc)
            .FirstOrDefaultAsync(ct);

        if (lastSnapshot == null)
            return true;

        return lastSnapshot.AtUtc.Date < today;
    }

    private async Task ResetDailyEquityAsync(CancellationToken ct)
    {
        var settings = await _db.BotSettings.FirstAsync(ct);
        var latestSnapshot = await _db.EquitySnapshotEntity
            .OrderByDescending(s => s.AtUtc)
            .FirstOrDefaultAsync(ct);

        var currentEquity = latestSnapshot?.TotalEquityUsdt ?? 0;

        settings.EquityStartOfDayUsdt = currentEquity;
        settings.PauseUntilUtc = null;
        settings.StopUntilUtc = null;

        await _db.SaveChangesAsync(ct);
        await LogRiskEventAsync("RISK_RESET", $"New day - ${currentEquity:N2}", ct);
    }

    private async Task LogRiskEventAsync(string type, string message, CancellationToken ct)
    {
        _db.Events.Add(new EventEntity
        {
            Level = "WARN",
            Type = type,
            Message = message,
            AtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }
}

public sealed class CanEnterResult
{
    public bool CanEnter { get; set; }
    public string Reason { get; set; } = "";
    public RiskStateDto State { get; set; } = new();
}