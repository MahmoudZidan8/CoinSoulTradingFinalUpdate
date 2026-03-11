namespace CoinSoul.Trading.Engine.Cache;

public sealed class MarketDataCacheOptions
{
    public const string SectionName = "MarketDataCache";
    public int TickerTtlMs { get; set; }
    public int BookTtlMs { get; set; }
    public int KlinesTtlMs { get; set; }
    public int ExchangeInfoTtlMinutes { get; set; }
    public bool EnableCacheLogging { get; set; }
    public int MaxStalenessMs { get; set; }
    public int AllTickersTtlMs { get; set; }

    // Add this property to fix CS1061
    public int ExchangeInfoTtlMs => ExchangeInfoTtlMinutes * 60 * 1000;
}