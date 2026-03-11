using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Core;

/// <summary>
/// Service for writing account trades to the database with duplicate handling
/// </summary>
public sealed class AccountTradeWriter : IAccountTradeWriter
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<AccountTradeWriter> _logger;

    public AccountTradeWriter(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<AccountTradeWriter> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Saves an account trade to the database.
    /// Ignores if TradeId already exists (idempotent).
    /// Does not throw on duplicate keys.
    /// </summary>
    public async Task<bool> SaveAsync(AccountTradeEntity trade, CancellationToken ct = default)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            // Check if trade already exists
            var exists = await db.AccountTrades
                .AnyAsync(t => t.TradeId == trade.TradeId, ct);

            if (exists)
            {
                _logger.LogDebug(
                    "Trade {TradeId} for {Symbol} already exists - skipping",
                    trade.TradeId,
                    trade.Symbol);
                return false;
            }

            // Insert new trade
            db.AccountTrades.Add(trade);
            await db.SaveChangesAsync(ct);

            _logger.LogDebug(
                "Saved trade {TradeId} for {Symbol} {Side} @ {Price}",
                trade.TradeId,
                trade.Symbol,
                trade.Side,
                trade.Price);

            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Race condition: another process inserted the same trade
            _logger.LogDebug(
                "Duplicate key for TradeId {TradeId} - ignoring",
                trade.TradeId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to save trade {TradeId} for {Symbol}: {Error}",
                trade.TradeId,
                trade.Symbol,
                ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Saves multiple account trades in a batch.
    /// Skips duplicates silently.
    /// </summary>
    public async Task<int> SaveBatchAsync(IEnumerable<AccountTradeEntity> trades, CancellationToken ct = default)
    {
        var savedCount = 0;
        var tradesList = trades.ToList();

        if (tradesList.Count == 0)
            return 0;

        try
        {

            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            
            // Get existing trade IDs
            var tradeIds = tradesList.Select(t => t.TradeId).ToList();
            var existingIds = await db.AccountTrades
                .Where(t => tradeIds.Contains(t.TradeId))
                .Select(t => t.TradeId)
                .ToListAsync(ct);

            var existingSet = new HashSet<long>(existingIds);

            // Filter out duplicates
            var newTrades = tradesList
                .Where(t => !existingSet.Contains(t.TradeId))
                .ToList();

            if (newTrades.Count == 0)
            {
                _logger.LogDebug("All {Count} trades already exist - skipping batch", tradesList.Count);
                return 0;
            }

            // Insert new trades
            db.AccountTrades.AddRange(newTrades);
            savedCount = await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "Saved {SavedCount} of {TotalCount} trades (skipped {SkippedCount} duplicates)",
                savedCount,
                tradesList.Count,
                tradesList.Count - savedCount);

            return savedCount;
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            // Partial batch conflict - try individual saves
            _logger.LogWarning("Batch save had conflicts - falling back to individual saves");

            foreach (var trade in tradesList)
            {
                if (await SaveAsync(trade, ct))
                    savedCount++;
            }

            return savedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to save trade batch of {Count} trades: {Error}",
                tradesList.Count,
                ex.Message);

            // Try individual saves as fallback
            foreach (var trade in tradesList)
            {
                if (await SaveAsync(trade, ct))
                    savedCount++;
            }

            return savedCount;
        }
    }

    /// <summary>
    /// Checks if exception is due to duplicate key constraint
    /// </summary>
    private static bool IsDuplicateKeyException(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("UNIQUE constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("PRIMARY KEY constraint", StringComparison.OrdinalIgnoreCase) ||
               message.Contains("PK_AccountTrades", StringComparison.OrdinalIgnoreCase);
    }
}