namespace CoinSoul.Trading.Core;

public sealed class TradeHistoryItem
{
    public string Symbol { get; set; } = "";

    public decimal EntryPrice { get; set; }
    public decimal ExitPrice { get; set; }
    public decimal Quantity { get; set; }

    public decimal PnL { get; set; }

    public DateTime OpenedAtUtc { get; set; }
    public DateTime ClosedAtUtc { get; set; }   // ✅ ده اللي ناقص عندك

    public string ExitReason { get; set; } = "";
}
