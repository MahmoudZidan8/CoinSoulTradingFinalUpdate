namespace CoinSoul.Entities;

public sealed class PortfolioSummary
{
    public decimal CurrentEquityUsdt { get; set; }
    public decimal FreeUsdt { get; set; }
    public decimal LockedUsdt { get; set; }
}

public sealed class RiskState
{
    public string Status { get; set; } = "SAFE";
    public string StatusColor { get; set; } = "green";
    public decimal CurrentEquityUsdt { get; set; }
    public decimal StartOfDayEquityUsdt { get; set; }
    public decimal DrawdownPct { get; set; }
    public DateTime? PauseUntilUtc { get; set; }
    public DateTime? StopUntilUtc { get; set; }
    public string Message { get; set; } = "";
}

public sealed class DashboardPortfolioState
{
    public bool Success { get; set; }
    public PortfolioSummary Portfolio { get; set; } = new();
    public RiskState Risk { get; set; } = new();
}