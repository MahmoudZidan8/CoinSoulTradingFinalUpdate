using CoinSoul.Entities;
using CoinSoul.Trading.Core;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Service for managing portfolio state and snapshots
/// </summary>
public sealed class PortfolioStateService
{
    private readonly IPortfolioService _portfolio;
    private readonly RiskGuardService _riskGuard;
    private readonly CoinSoulDbContext _db;

    public PortfolioStateService(
        IPortfolioService portfolio,
        RiskGuardService riskGuard,
        CoinSoulDbContext db)
    {
        _portfolio = portfolio;
        _riskGuard = riskGuard;
        _db = db;
    }

    /// <summary>
    /// Gets complete dashboard state (Portfolio + Risk)
    /// </summary>
    public async Task<DashboardPortfolioStateDto> GetDashboardStateAsync(CancellationToken ct)
    {
        var portfolio = await _portfolio.GetPortfolioAsync(ct);
        var riskState = await _riskGuard.GetRiskStateAsync(ct);

        return new DashboardPortfolioStateDto
        {
            Success = true,
            Portfolio = portfolio,
            Risk = riskState
        };
    }

    /// <summary>
    /// Saves periodic equity snapshot to database
    /// </summary>
    public async Task SavePeriodicSnapshotAsync(CancellationToken ct)
    {
        var portfolio = await _portfolio.GetPortfolioAsync(ct);

        var snapshot = new EquitySnapshotEntity
        {
            AtUtc = DateTime.UtcNow,
            DayUtc = DateTime.UtcNow.Date,
            TotalEquityUsdt = portfolio.TotalEquityUsdt,
            FreeUsdt = portfolio.FreeUsdt,
            LockedUsdt = portfolio.LockedUsdt,
            StartOfDayEquityUsdt = portfolio.StartOfDayEquityUsdt,
            TopHoldings = System.Text.Json.JsonSerializer.Serialize(portfolio.Holdings.Take(5))
        };

        _db.EquitySnapshotEntity.Add(snapshot);
        await _db.SaveChangesAsync(ct);
    }
}