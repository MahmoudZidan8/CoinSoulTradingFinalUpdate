namespace CoinSoul.Trading.Core;

public sealed class DashboardStats
{
    // Counts
    public int TotalTrades { get; set; }
    public int TradesCount { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public decimal WinRatePct { get; set; }

    // PnL
    public decimal NetPnlUsdt { get; set; }
    public decimal AvgPnlUsdt { get; set; }
    public decimal AvgWinUsdt { get; set; }
    public decimal AvgLossUsdt { get; set; }
    public decimal BestTradeUsdt { get; set; }
    public decimal WorstTradeUsdt { get; set; }

    // Risk
    public decimal MaxDrawdownUsdt { get; set; }
    public decimal ProfitFactor { get; set; }
    public decimal ExpectancyUsdt { get; set; }

    // State
    public int OpenPositions { get; set; }

    // Last Event
    public DateTime? LastEventAtUtc { get; set; }
    public string? LastEventMessage { get; set; }
}
