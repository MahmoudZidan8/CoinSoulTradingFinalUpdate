namespace CoinSoul.Trading.Core;

/// <summary>
/// Contract for portfolio data retrieval services
/// </summary>
public interface IPortfolioService
{
    /// <summary>
    /// Gets current portfolio summary including equity and holdings
    /// </summary>
    Task<PortfolioDto> GetPortfolioAsync(CancellationToken ct = default);
}