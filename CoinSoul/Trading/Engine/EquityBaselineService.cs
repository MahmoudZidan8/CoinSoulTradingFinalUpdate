using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Ensures StartOfDayEquity baseline is set for risk calculations
/// </summary>
public sealed class EquityBaselineService
{
    private readonly IPortfolioService _portfolio;
    private readonly CoinSoulDbContext _db;
    private DateTime _lastBaselineCheck = DateTime.MinValue;

    public EquityBaselineService(IPortfolioService portfolio, CoinSoulDbContext db)
    {
        _portfolio = portfolio;
        _db = db;
    }

    /// <summary>
    /// Ensures StartOfDayEquity is set. Call once per tick or on bot start.
    /// </summary>
    public async Task EnsureBaselineAsync(CancellationToken ct)
    {
        var nowUtc = DateTime.UtcNow;
        var today = nowUtc.Date;

        // Only check once per day
        if (_lastBaselineCheck.Date == today)
            return;

        _lastBaselineCheck = nowUtc;

        // Check if today's baseline exists
        var todayBaseline = await _db.EquitySnapshotEntity
            .Where(s => s.DayUtc == today && s.IsStartOfDay)
            .FirstOrDefaultAsync(ct);

        if (todayBaseline != null)
            return; // Already set

        // Get current equity
        var portfolio = await _portfolio.GetPortfolioAsync(ct);
        var currentEquity = portfolio.TotalEquityUsdt;

        // Create baseline snapshot
        var baseline = new EquitySnapshotEntity
        {
            AtUtc = nowUtc,
            DayUtc = today,
            TotalEquityUsdt = currentEquity,
            StartOfDayEquityUsdt = currentEquity,
            FreeUsdt = portfolio.FreeUsdt,
            LockedUsdt = portfolio.LockedUsdt,
            IsStartOfDay = true, // NEW FLAG (add to entity)
            TopHoldings = System.Text.Json.JsonSerializer.Serialize(portfolio.Holdings.Take(5))
        };

        _db.EquitySnapshotEntity.Add(baseline);
        await _db.SaveChangesAsync(ct);

        // Log event
        _db.Events.Add(new EventEntity
        {
            AtUtc = nowUtc,
            Level = "INFO",
            Type = "BASELINE_INIT",
            Message = $"StartOfDayEquity set: ${currentEquity:N2} for {today:yyyy-MM-dd}"
        });
        await _db.SaveChangesAsync(ct);
    }
}