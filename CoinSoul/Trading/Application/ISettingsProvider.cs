using CoinSoul.Entities;

namespace CoinSoul.Trading.Application;

/// <summary>
/// Provides settings snapshot for trading operations
/// </summary>
public interface ISettingsProvider
{
    Task<BotSettingsEntity?> GetSettingsSnapshotAsync(CancellationToken ct);
}