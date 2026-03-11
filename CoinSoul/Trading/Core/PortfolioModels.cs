namespace CoinSoul.Trading.Core;

/// <summary>
/// DTO for portfolio summary data transfer
/// </summary>
public sealed class PortfolioDto
{
    public decimal TotalEquityUsdt { get; set; }
    public decimal FreeUsdt { get; set; }
    public decimal LockedUsdt { get; set; } = 0;
    public decimal StartOfDayEquityUsdt { get; set; }

    public List<PortfolioHoldingDto> Holdings { get; set; } = new();
}

/// <summary>
/// DTO for individual asset holding
/// </summary>
public sealed class PortfolioHoldingDto
{
    public string Asset { get; set; } = "";
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total => Free + Locked;
    public decimal UsdtValue { get; set; }
}