namespace CoinSoul.Trading.Core;

public sealed class AccountTradeRow
{
    public DateTime TradeTimeUtc { get; set; }
    public string Symbol { get; set; } = "";
    public string Side { get; set; } = ""; // BUY or SELL
    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuoteQty { get; set; }
    public decimal Commission { get; set; }
    public string CommissionAsset { get; set; } = "";
    public long OrderId { get; set; }
    public long TradeId { get; set; }
    public bool IsBuyer { get; set; }
    public bool IsMaker { get; set; }

    public string TradeTimeLocal => TradeTimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
    
    public decimal NetPnL => IsBuyer 
        ? -(QuoteQty + (CommissionAsset == "USDT" ? Commission : 0)) 
        : (QuoteQty - (CommissionAsset == "USDT" ? Commission : 0));
}