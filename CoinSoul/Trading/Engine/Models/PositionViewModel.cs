using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine.Models;

public sealed class PositionViewModel
{
    public string Symbol { get; set; } = "";
    public decimal EntryPrice { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal Quantity { get; set; }
    public decimal GrossPnL { get; set; }
    public decimal NetPnL { get; set; }
    public decimal PnLPct { get; set; }
    public decimal? TP { get; set; }
    public decimal? SL { get; set; }
    public string LifecycleState { get; set; } = "";
    public DateTime OpenTime { get; set; }
    public int TimeInTradeSeconds { get; set; }
    public decimal RiskAtEntry { get; set; }
    public string RegimeAtEntry { get; set; } = "";
    public string ExitType { get; set; } = "";
    public bool OcoPlaced { get; set; }

    public static PositionViewModel FromPaperPosition(PaperPosition p, decimal currentPrice, DateTime nowUtc)
    {
        var grossPnl = p.UnrealizedPnL(currentPrice);
        var netPnl = p.NetUnrealizedPnL(currentPrice);
        var pnlPct = p.EntryPrice > 0 ? ((currentPrice - p.EntryPrice) / p.EntryPrice) * 100m : 0;
        var timeInTrade = (int)(nowUtc - p.EntryTime).TotalSeconds;

        return new PositionViewModel
        {
            Symbol = p.Symbol,
            EntryPrice = p.EntryPrice,
            CurrentPrice = currentPrice,
            Quantity = p.RemainingQuantity,
            GrossPnL = grossPnl,
            NetPnL = netPnl,
            PnLPct = pnlPct,
            TP = p.TakeProfitPrice,
            SL = p.StopLossPrice,
            LifecycleState = p.LifecycleState,
            OpenTime = p.EntryTime,
            TimeInTradeSeconds = timeInTrade,
            RiskAtEntry = p.RiskAtEntry,
            RegimeAtEntry = p.RegimeAtEntry,
            ExitType = p.ExitType,
            OcoPlaced = p.OcoPlaced
        };
    }

    public static PositionViewModel FromDbPosition(
        CoinSoul.Entities.PositionEntity dbPos, 
        decimal currentPrice, 
        DateTime nowUtc)
    {
        var grossPnl = (currentPrice - dbPos.EntryPrice) * dbPos.Quantity;
        var netPnl = grossPnl - dbPos.FeesPaidUsd;
        var pnlPct = dbPos.EntryPrice > 0 ? ((currentPrice - dbPos.EntryPrice) / dbPos.EntryPrice) * 100m : 0;
        var timeInTrade = (int)(nowUtc - dbPos.OpenedAtUtc).TotalSeconds;

        var lifecycleState = dbPos.Stage switch
        {
            0 => "ENTRY_PENDING",
            1 => "ENTRY_FILLED",
            2 => "OCO_PLACING",
            3 => "OCO_PLACED",
            4 => "CLOSING",
            5 => "CLOSED",
            99 => "FAILED",
            _ => "UNKNOWN"
        };

        return new PositionViewModel
        {
            Symbol = dbPos.Symbol,
            EntryPrice = dbPos.EntryPrice,
            CurrentPrice = currentPrice,
            Quantity = dbPos.Quantity,
            GrossPnL = grossPnl,
            NetPnL = netPnl,
            PnLPct = pnlPct,
            TP = null,
            SL = null,
            LifecycleState = lifecycleState,
            OpenTime = dbPos.OpenedAtUtc,
            TimeInTradeSeconds = timeInTrade,
            RiskAtEntry = 0,
            RegimeAtEntry = "DB",
            ExitType = dbPos.CloseReason ?? "",
            OcoPlaced = dbPos.OcoOrderId.HasValue
        };
    }
}