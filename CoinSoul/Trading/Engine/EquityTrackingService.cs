using CoinSoul.Entities;
using CoinSoul.Trading.Core;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Background service for tracking equity snapshots
/// </summary>
public sealed class EquityTrackingService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<EquityTrackingService> _logger;

    public EquityTrackingService(IServiceScopeFactory scopeFactory, ILogger<EquityTrackingService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(10), ct);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await TrackEquityAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking equity");
            }

            await Task.Delay(TimeSpan.FromSeconds(30), ct);
        }
    }

    private async Task TrackEquityAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();
        var portfolio = scope.ServiceProvider.GetRequiredService<IPortfolioService>();

        var now = DateTime.UtcNow;
        var currentMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0);

        // Check for duplicate within same minute
        var exists = await db.EquitySnapshotEntity
            .AnyAsync(s => s.AtUtc >= currentMinute && s.AtUtc < currentMinute.AddMinutes(1), ct);

        if (exists)
        {
            return;
        }

        var portfolioData = await portfolio.GetPortfolioAsync(ct);

        var snapshot = new EquitySnapshotEntity
        {
            AtUtc = now,
            DayUtc = now.Date,
            TotalEquityUsdt = portfolioData.TotalEquityUsdt,
            FreeUsdt = portfolioData.FreeUsdt,
            LockedUsdt = portfolioData.LockedUsdt,
            StartOfDayEquityUsdt = portfolioData.StartOfDayEquityUsdt,
            TopHoldings = System.Text.Json.JsonSerializer.Serialize(portfolioData.Holdings.Take(5))
        };

        db.EquitySnapshotEntity.Add(snapshot);
        await db.SaveChangesAsync(ct);
    }
}