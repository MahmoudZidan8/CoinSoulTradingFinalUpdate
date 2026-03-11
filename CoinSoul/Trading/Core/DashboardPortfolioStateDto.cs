namespace CoinSoul.Trading.Core;

/// <summary>
/// DTO for complete dashboard state (Portfolio + Risk)
/// </summary>
public sealed record DashboardPortfolioStateDto
{
    public bool Success { get; init; }
    public PortfolioDto Portfolio { get; init; } = new();
    public RiskStateDto Risk { get; init; } = new();
}