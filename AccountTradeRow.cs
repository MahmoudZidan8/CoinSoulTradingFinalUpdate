namespace CoinSoul.Trading.Core
{
    public class AccountTradeRow
    {
        public DateTime TradeTimeLocal { get; set; }
        public string Symbol { get; set; }
        public string Side { get; set; }
        public bool IsMaker { get; set; }
        public decimal Price { get; set; }
        public decimal Quantity { get; set; }
        public decimal QuoteQty { get; set; }
        public decimal Commission { get; set; }
        public string CommissionAsset { get; set; }
        public decimal NetPnL { get; set; }
        public long OrderId { get; set; }
    }
}