using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Hosted;

/// <summary>
/// Background service that periodically cleans up old database records
/// Runs every 6 hours to maintain database performance and size
/// </summary>
public sealed class DbRetentionHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DbRetentionHostedService> _logger;

    // Run cleanup every 6 hours
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(6);

    // Retention periods
    private static readonly TimeSpan EventsRetention = TimeSpan.FromDays(30);
    private static readonly TimeSpan OrdersRetention = TimeSpan.FromDays(60);

    public DbRetentionHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<DbRetentionHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("DB Retention Service started. Cleanup runs every {Hours} hours.", CleanupInterval.TotalHours);

        // Wait 1 minute after startup before first cleanup
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformCleanupAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during database cleanup");
            }

            // Wait for next cleanup cycle
            try
            {
                await Task.Delay(CleanupInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Expected during shutdown
                break;
            }
        }

        _logger.LogInformation("DB Retention Service stopped.");
    }

    private async Task PerformCleanupAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting database cleanup...");

        var startTime = DateTime.UtcNow;
        var totalDeleted = 0;

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();

        // ========== 1) CLEAN UP OLD EVENTS ==========
        var eventsDeletedCount = await CleanupEventsAsync(db, ct);
        totalDeleted += eventsDeletedCount;

        // ========== 2) CLEAN UP OLD ORDERS ==========
        var ordersDeletedCount = await CleanupOrdersAsync(db, ct);
        totalDeleted += ordersDeletedCount;

        var duration = DateTime.UtcNow - startTime;

        _logger.LogInformation(
            "Database cleanup completed in {Duration}s. Deleted: {Events} events, {Orders} orders (Total: {Total})",
            duration.TotalSeconds,
            eventsDeletedCount,
            ordersDeletedCount,
            totalDeleted);
    }

    /// <summary>
    /// Delete events older than 30 days
    /// </summary>
    private async Task<int> CleanupEventsAsync(CoinSoulDbContext db, CancellationToken ct)
    {
        var cutoffDate = DateTime.UtcNow.Add(-EventsRetention);

        _logger.LogDebug("Deleting events older than {Date} ({Days} days)...", cutoffDate, EventsRetention.TotalDays);

        try
        {
            // Use ExecuteDeleteAsync for efficient bulk delete (EF Core 7+)
            var deleted = await db.Events
                .Where(e => e.AtUtc < cutoffDate)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
            {
                _logger.LogInformation("Deleted {Count} old events (older than {Days} days)", deleted, EventsRetention.TotalDays);
            }
            else
            {
                _logger.LogDebug("No old events to delete");
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup events");
            return 0;
        }
    }

    /// <summary>
    /// Delete orders older than 60 days
    /// </summary>
    private async Task<int> CleanupOrdersAsync(CoinSoulDbContext db, CancellationToken ct)
    {
        var cutoffDate = DateTime.UtcNow.Add(-OrdersRetention);

        _logger.LogDebug("Deleting orders older than {Date} ({Days} days)...", cutoffDate, OrdersRetention.TotalDays);

        try
        {
            // Use ExecuteDeleteAsync for efficient bulk delete (EF Core 7+)
            var deleted = await db.Orders
                .Where(o => o.AtUtc < cutoffDate)
                .ExecuteDeleteAsync(ct);

            if (deleted > 0)
            {
                _logger.LogInformation("Deleted {Count} old orders (older than {Days} days)", deleted, OrdersRetention.TotalDays);
            }
            else
            {
                _logger.LogDebug("No old orders to delete");
            }

            return deleted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cleanup orders");
            return 0;
        }
    }
}