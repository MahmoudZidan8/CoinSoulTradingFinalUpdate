namespace CoinSoul.Trading.Core;

public interface ITradingStrategy
{
    Task EvaluateAsync(
        BotMarketSnapshot market,
        BotState state,
        CancellationToken ct);
}
