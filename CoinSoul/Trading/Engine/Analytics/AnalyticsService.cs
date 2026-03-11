using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Analytics;

public sealed class AnalyticsService
{
    private readonly CoinSoulDbContext _db;
    private readonly ILogger<AnalyticsService> _logger;

    public AnalyticsService(CoinSoulDbContext db, ILogger<AnalyticsService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<PerformanceDashboardDto> GetDashboardAsync(DateTimeOffset nowUtc, CancellationToken ct)
    {
        var todayStart = new DateTimeOffset(nowUtc.Date, TimeSpan.Zero);
        var last7Start = todayStart.AddDays(-6);
        var last30Start = todayStart.AddDays(-29);

        var dashboard = new PerformanceDashboardDto
        {
            GeneratedAtUtc = nowUtc
        };

        try
        {
            dashboard.Today = await ComputePeriodMetricsAsync(todayStart, nowUtc, ct);
            dashboard.Last7Days = await ComputePeriodMetricsAsync(last7Start, nowUtc, ct);
            dashboard.Last30Days = await ComputePeriodMetricsAsync(last30Start, nowUtc, ct);

            dashboard.Drawdown = await ComputeDrawdownAsync(last30Start, nowUtc, ct);
            dashboard.ExecutionQuality = await ComputeExecutionQualityAsync(last7Start, nowUtc, ct);
            dashboard.RegimeStats = await ComputeRegimeStatsAsync(last7Start, nowUtc, ct);

            dashboard.EquityCurveToday = await GetEquityCurveAsync(todayStart, nowUtc, ct);
            dashboard.EquityCurve7D = await GetEquityCurveAsync(last7Start, nowUtc, ct);
            dashboard.EquityCurve30D = await GetEquityCurveAsync(last30Start, nowUtc, ct);

            dashboard.TopWinners7D = await GetTopSymbolsAsync(last7Start, nowUtc, isWinners: true, ct);
            dashboard.TopLosers7D = await GetTopSymbolsAsync(last7Start, nowUtc, isWinners: false, ct);

            dashboard.TopRejectReasonsToday = await GetTopRejectReasonsAsync(todayStart, nowUtc, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error computing analytics dashboard");
        }

        return dashboard;
    }

    private async Task<PeriodMetricsDto> ComputePeriodMetricsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var closedPositions = await _db.Positions
            .AsNoTracking()
            .Where(p => !p.IsOpen && p.ClosedAtUtc >= start.UtcDateTime && p.ClosedAtUtc <= end.UtcDateTime)
            .ToListAsync(ct);

        var trades = closedPositions.Count;
        var wins = closedPositions.Count(p => p.NetPnlUsdt > 0);
        var losses = closedPositions.Count(p => p.NetPnlUsdt < 0);

        var grossPnl = closedPositions.Sum(p => p.NetPnlUsdt);
        var fees = closedPositions.Sum(p => p.FeesUsdt);
        var netPnl = grossPnl;

        var maxWin = closedPositions.Any() ? closedPositions.Max(p => p.NetPnlUsdt) : 0;
        var maxLoss = closedPositions.Any() ? closedPositions.Min(p => p.NetPnlUsdt) : 0;

        return new PeriodMetricsDto
        {
            Trades = trades,
            Wins = wins,
            Losses = losses,
            WinRatePct = trades > 0 ? (decimal)wins / trades * 100 : 0,
            GrossPnlUsdt = grossPnl,
            FeesUsdt = fees,
            NetPnlUsdt = netPnl,
            AvgNetPnlUsdt = trades > 0 ? netPnl / trades : 0,
            MaxWinUsdt = maxWin,
            MaxLossUsdt = maxLoss
        };
    }

    private async Task<DrawdownDto> ComputeDrawdownAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var snapshots = await _db.EquitySnapshotEntity
            .AsNoTracking()
            .Where(s => s.AtUtc >= start.UtcDateTime && s.AtUtc <= end.UtcDateTime)
            .OrderBy(s => s.AtUtc)
            .ToListAsync(ct);

        if (!snapshots.Any())
        {
            return new DrawdownDto();
        }

        var startEquity = snapshots.First().TotalEquityUsdt;
        var currentEquity = snapshots.Last().TotalEquityUsdt;
        var maxEquity = snapshots.Max(s => s.TotalEquityUsdt);

        var maxDrawdownPct = 0m;
        var peak = snapshots.First().TotalEquityUsdt;

        foreach (var snap in snapshots)
        {
            if (snap.TotalEquityUsdt > peak)
                peak = snap.TotalEquityUsdt;

            var drawdown = peak > 0 ? ((peak - snap.TotalEquityUsdt) / peak) * 100 : 0;
            if (drawdown > maxDrawdownPct)
                maxDrawdownPct = drawdown;
        }

        var currentDrawdownPct = maxEquity > 0 ? ((maxEquity - currentEquity) / maxEquity) * 100 : 0;

        return new DrawdownDto
        {
            StartEquityUsdt = startEquity,
            CurrentEquityUsdt = currentEquity,
            MaxEquityUsdt = maxEquity,
            MaxDrawdownPct = maxDrawdownPct,
            CurrentDrawdownPct = currentDrawdownPct
        };
    }

    private async Task<ExecutionQualityDto> ComputeExecutionQualityAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var events = await _db.Events
            .AsNoTracking()
            .Where(e => e.AtUtc >= start.UtcDateTime && e.AtUtc <= end.UtcDateTime)
            .ToListAsync(ct);

        var entryRejected = events.Count(e => e.Type.Contains("ENTRY_REJECT") || e.Type.Contains("ENTRY_BLOCKED"));
        var entryAccepted = events.Count(e => e.Type == "ENTRY" || e.Type == "ENTRY_FILLED");
        var entryAttempts = entryRejected + entryAccepted;

        var ocoAttempts = events.Count(e => e.Type == "OCO_ATTEMPT");
        var ocoOk = events.Count(e => e.Type == "OCO_OK");
        var ocoFail = events.Count(e => e.Type == "OCO_FAIL");
        var safetyExits = events.Count(e => e.Type == "SAFETY_EXIT");

        var ocoTotal = ocoOk + ocoFail;
        var ocoSuccessRate = ocoTotal > 0 ? (decimal)ocoOk / ocoTotal * 100 : 0;

        return new ExecutionQualityDto
        {
            EntryAttempts = entryAttempts,
            EntryAccepted = entryAccepted,
            EntryRejected = entryRejected,
            OcoPlaced = ocoAttempts,
            OcoOk = ocoOk,
            OcoFail = ocoFail,
            SafetyExits = safetyExits,
            OcoSuccessRatePct = ocoSuccessRate
        };
    }

    private async Task<RegimeStatsDto> ComputeRegimeStatsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var regimeEvents = await _db.Events
            .AsNoTracking()
            .Where(e => e.AtUtc >= start.UtcDateTime && e.AtUtc <= end.UtcDateTime && e.Type == "MARKET_REGIME")
            .ToListAsync(ct);

        var bullCount = regimeEvents.Count(e => e.Message.Contains("BullTrend"));
        var bearCount = regimeEvents.Count(e => e.Message.Contains("BearTrend"));
        var sidewaysCount = regimeEvents.Count(e => e.Message.Contains("Sideways"));
        var crashCount = regimeEvents.Count(e => e.Message.Contains("Crash"));

        var total = bullCount + bearCount + sidewaysCount + crashCount;

        return new RegimeStatsDto
        {
            BullCount = bullCount,
            BearCount = bearCount,
            SidewaysCount = sidewaysCount,
            CrashCount = crashCount,
            BullPct = total > 0 ? (decimal)bullCount / total * 100 : 0,
            BearPct = total > 0 ? (decimal)bearCount / total * 100 : 0,
            SidewaysPct = total > 0 ? (decimal)sidewaysCount / total * 100 : 0,
            CrashPct = total > 0 ? (decimal)crashCount / total * 100 : 0
        };
    }

    private async Task<List<EquityPointDto>> GetEquityCurveAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var snapshots = await _db.EquitySnapshotEntity
            .AsNoTracking()
            .Where(s => s.AtUtc >= start.UtcDateTime && s.AtUtc <= end.UtcDateTime)
            .OrderBy(s => s.AtUtc)
            .Select(s => new EquityPointDto
            {
                AtUtc = new DateTimeOffset(s.AtUtc, TimeSpan.Zero),
                EquityUsdt = s.TotalEquityUsdt
            })
            .ToListAsync(ct);

        return snapshots;
    }

    private async Task<List<TopSymbolPnlDto>> GetTopSymbolsAsync(DateTimeOffset start, DateTimeOffset end, bool isWinners, CancellationToken ct)
    {
        var positions = await _db.Positions
            .AsNoTracking()
            .Where(p => !p.IsOpen && p.ClosedAtUtc >= start.UtcDateTime && p.ClosedAtUtc <= end.UtcDateTime)
            .GroupBy(p => p.Symbol)
            .Select(g => new TopSymbolPnlDto
            {
                Symbol = g.Key,
                NetPnlUsdt = g.Sum(p => p.NetPnlUsdt),
                Trades = g.Count()
            })
            .ToListAsync(ct);

        var filtered = isWinners
            ? positions.Where(p => p.NetPnlUsdt > 0).OrderByDescending(p => p.NetPnlUsdt).Take(10)
            : positions.Where(p => p.NetPnlUsdt < 0).OrderBy(p => p.NetPnlUsdt).Take(10);

        return filtered.ToList();
    }

    private async Task<List<RejectReasonDto>> GetTopRejectReasonsAsync(DateTimeOffset start, DateTimeOffset end, CancellationToken ct)
    {
        var rejectEvents = await _db.Events
            .AsNoTracking()
            .Where(e => e.AtUtc >= start.UtcDateTime && e.AtUtc <= end.UtcDateTime 
                && (e.Type.Contains("REJECT") || e.Type.Contains("BLOCKED")))
            .ToListAsync(ct);

        var reasonGroups = rejectEvents
            .Select(e =>
            {
                var msg = e.Message;
                var colonIndex = msg.IndexOf(':');
                return colonIndex > 0 ? msg.Substring(0, colonIndex).Trim() : msg;
            })
            .GroupBy(r => r)
            .Select(g => new RejectReasonDto
            {
                Reason = g.Key,
                Count = g.Count()
            })
            .OrderByDescending(r => r.Count)
            .Take(10)
            .ToList();

        return reasonGroups;
    }
}