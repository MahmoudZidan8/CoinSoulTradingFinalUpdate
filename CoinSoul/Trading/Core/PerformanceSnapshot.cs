namespace CoinSoul.Trading.Core;

public sealed class PerformanceSnapshot
{
    public int TotalTrades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }

    public decimal WinRatePct { get; set; }

    public decimal TotalPnL { get; set; }
    public decimal AvgWin { get; set; }
    public decimal AvgLoss { get; set; }

    public decimal MaxDrawdown { get; set; }
}
