using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public sealed class PositionGuardService
{
    private readonly CoinSoulDbContext _db;

    public PositionGuardService(CoinSoulDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Checks if a new position can be opened for the given symbol
    /// </summary>
    public async Task<CanOpenPositionResult> CanOpenNewPositionAsync(string symbol, CancellationToken ct)
    {
        var settings = await _db.BotSettings.AsNoTracking().FirstAsync(ct);

        // Cleanup: release long-stuck EntryPending reservations (no filled buy).
        // This prevents the bot from getting stuck on MAX_POSITIONS due to "ghost" positions.
        var staleCutoff = DateTime.UtcNow.AddMinutes(-3);
        var stalePending = await _db.Positions
            .Where(p => p.IsActive && p.Stage == (int)TradeStage.EntryPending && p.OpenedAtUtc < staleCutoff)
            .ToListAsync(ct);
        if (stalePending.Count > 0)
        {
            foreach (var p in stalePending)
            {
                p.Stage = (int)TradeStage.Failed;
                p.IsActive = false;
                p.IsOpen = false;
                p.CloseReason = "ENTRY_TIMEOUT";
                p.LastError = "EntryPending exceeded timeout (no filled buy).";
            }
            await _db.SaveChangesAsync(ct);
        }

        // Count ONLY positions that are still OPEN.
        // Using IsActive alone can leave the bot stuck on MAX_POSITIONS
        // if old positions were marked closed (IsOpen=false) but remained active.
        var activePositions = await _db.Positions
            .Where(p => p.IsActive && p.IsOpen && p.Quantity > 0 && p.Stage != (int)TradeStage.EntryPending)
            .ToListAsync(ct);

        var activeCount = activePositions.Count;
        var maxPositions = settings.MaxOpenTrades > 0 ? settings.MaxOpenTrades : settings.MaxConcurrentPositions;

        if (maxPositions > 0 && activeCount >= maxPositions)
        {
            return new CanOpenPositionResult
            {
                CanOpen = false,
                Reason = $"Max positions reached: {activeCount}/{maxPositions}",
                BlockReason = "MAX_POSITIONS"
            };
        }

        if (settings.BlockSameSymbolReentry)
        {
            var hasActiveSymbol = activePositions.Any(p => p.Symbol.Equals(symbol, StringComparison.OrdinalIgnoreCase));
            
            if (hasActiveSymbol)
            {
                return new CanOpenPositionResult
                {
                    CanOpen = false,
                    Reason = $"Symbol {symbol} already has active position",
                    BlockReason = "SAME_SYMBOL"
                };
            }
        }

        return new CanOpenPositionResult
        {
            CanOpen = true,
            Reason = "OK",
            BlockReason = null
        };
    }
}

public sealed class CanOpenPositionResult
{
    public bool CanOpen { get; set; }
    public string Reason { get; set; } = "";
    public string? BlockReason { get; set; }
}