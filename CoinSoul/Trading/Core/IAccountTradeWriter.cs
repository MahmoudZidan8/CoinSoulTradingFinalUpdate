using CoinSoul.Entities;

namespace CoinSoul.Trading.Core;

/// <summary>
/// Service for writing account trades to the database
/// </summary>
public interface IAccountTradeWriter
{
    /// <summary>
    /// Saves an account trade to the database.
    /// Ignores if TradeId already exists (idempotent).
    /// Does not throw on duplicate keys.
    /// </summary>
    /// <param name="trade">The trade entity to save</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>True if saved, False if duplicate or failed</returns>
    Task<bool> SaveAsync(AccountTradeEntity trade, CancellationToken ct = default);

    /// <summary>
    /// Saves multiple account trades in a batch.
    /// Skips duplicates silently.
    /// </summary>
    /// <param name="trades">Collection of trades to save</param>
    /// <param name="ct">Cancellation token</param>
    /// <returns>Number of trades successfully saved</returns>
    Task<int> SaveBatchAsync(IEnumerable<AccountTradeEntity> trades, CancellationToken ct = default);
}