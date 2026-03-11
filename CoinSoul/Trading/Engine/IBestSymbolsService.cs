namespace CoinSoul.Trading.Core;

public interface IBestSymbolsService
{
    Task<IReadOnlyList<string>> GetBestSymbolsAsync(
        StrategyAMode mode,
        int count,
        CancellationToken ct);
}
