namespace CoinSoul.Trading.Core;

public sealed class PaperPosition
{
    public string Symbol { get; set; } = "";
    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }
    public decimal InitialQuantity { get; set; }
    public decimal RemainingQuantity { get; set; }
    public DateTime EntryTime { get; set; } = DateTime.UtcNow;
    public DateTime? ExitTime { get; set; }
    public bool IsClosed { get; set; }

    public decimal? TakeProfitPrice { get; set; }
    public decimal? StopLossPrice { get; set; }

    // ✅ NEW: Lifecycle tracking
    public string LifecycleState { get; set; } = "ENTRY_FILLED";
    public decimal EntryFeePaid { get; set; }
    public decimal ExitFeePaid { get; set; }
    public decimal SlippageCost { get; set; }
    public string RegimeAtEntry { get; set; } = "";
    public decimal RiskAtEntry { get; set; }
    public bool OcoPlaced { get; set; }
    public string ExitType { get; set; } = "";

    public decimal UnrealizedPnL(decimal currentPrice)
    {
        if (IsClosed) return 0;
        return (currentPrice - EntryPrice) * RemainingQuantity;
    }

    public decimal NetUnrealizedPnL(decimal currentPrice)
    {
        var gross = UnrealizedPnL(currentPrice);
        return gross - EntryFeePaid - SlippageCost;
    }

    public decimal? RealizedPnL()
    {
        if (ExitPrice is null) return null;
        return (ExitPrice.Value - EntryPrice) * InitialQuantity;
    }

    public decimal? NetRealizedPnL()
    {
        if (ExitPrice is null) return null;
        var gross = (ExitPrice.Value - EntryPrice) * InitialQuantity;
        return gross - EntryFeePaid - ExitFeePaid - SlippageCost;
    }
}
