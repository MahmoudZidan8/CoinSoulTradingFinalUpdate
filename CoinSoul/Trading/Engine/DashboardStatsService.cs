using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public sealed class DashboardStatsService
{
    private readonly CoinSoulDbContext _db;

    public DashboardStatsService(CoinSoulDbContext db)
    {
        _db = db;
    }

    public async Task<DashboardStats> GetAsync(CancellationToken ct)
    {
        var stats = new DashboardStats();

        var closed = await _db.Positions
            .AsNoTracking()
            .Where(p => !p.IsOpen && p.ClosedAtUtc != null)
            .OrderBy(p => p.ClosedAtUtc)
            .ToListAsync(ct);

        stats.TotalTrades = closed.Count;
        stats.OpenPositions = await _db.Positions.CountAsync(p => p.IsOpen, ct);

        if (closed.Count == 0)
            return stats;

        var wins = closed.Where(x => x.NetPnlUsdt > 0).ToList();
        var losses = closed.Where(x => x.NetPnlUsdt < 0).ToList();

        stats.Wins = wins.Count;
        stats.Losses = losses.Count;

        stats.WinRatePct = stats.TotalTrades == 0
            ? 0
            : (decimal)stats.Wins * 100m / stats.TotalTrades;

        stats.NetPnlUsdt = closed.Sum(x => x.NetPnlUsdt);
        stats.AvgPnlUsdt = stats.NetPnlUsdt / stats.TotalTrades;

        stats.AvgWinUsdt = wins.Count == 0 ? 0 : wins.Average(x => x.NetPnlUsdt);
        stats.AvgLossUsdt = losses.Count == 0 ? 0 : losses.Average(x => Math.Abs(x.NetPnlUsdt));

        stats.BestTradeUsdt = closed.Max(x => x.NetPnlUsdt);
        stats.WorstTradeUsdt = closed.Min(x => x.NetPnlUsdt);

        // ===== Max Drawdown =====
        decimal equity = 0;
        decimal peak = 0;
        decimal maxDd = 0;

        foreach (var t in closed)
        {
            equity += t.NetPnlUsdt;
            peak = Math.Max(peak, equity);
            var dd = peak - equity;
            maxDd = Math.Max(maxDd, dd);
        }

        stats.MaxDrawdownUsdt = maxDd;

        // ===== Profit Factor =====
        var grossWin = wins.Sum(x => x.NetPnlUsdt);
        var grossLoss = losses.Sum(x => Math.Abs(x.NetPnlUsdt));
        stats.ProfitFactor = grossLoss == 0 ? 0 : grossWin / grossLoss;

        // ===== Expectancy =====
        var winRate = stats.Wins / (decimal)stats.TotalTrades;
        var lossRate = 1 - winRate;
        stats.ExpectancyUsdt =
            (winRate * stats.AvgWinUsdt) -
            (lossRate * stats.AvgLossUsdt);

        // ===== Last Event =====
        var lastEvent = await _db.Events
            .AsNoTracking()
            .OrderByDescending(e => e.AtUtc)
            .FirstOrDefaultAsync(ct);

        stats.LastEventAtUtc = lastEvent?.AtUtc;
        stats.LastEventMessage = lastEvent == null
            ? null
            : $"[{lastEvent.Level}] {lastEvent.Type} - {lastEvent.Message}";

        return stats;
    }
}
