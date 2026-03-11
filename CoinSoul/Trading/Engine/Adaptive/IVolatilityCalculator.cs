namespace CoinSoul.Trading.Engine.Adaptive;

/// <summary>
/// Calculates market volatility metrics for adaptive scanning
/// </summary>
public interface IVolatilityCalculator
{
    /// <summary>
    /// Calculate current volatility percentage (ATR or price movement based)
    /// </summary>
    Task<decimal> CalculateVolatilityAsync(string symbol, CancellationToken ct);

    /// <summary>
    /// Get aggregate market volatility from top symbols
    /// </summary>
    Task<decimal> GetMarketVolatilityAsync(CancellationToken ct);
}