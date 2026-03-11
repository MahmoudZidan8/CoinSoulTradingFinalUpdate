using Binance.Net.Enums;
using Binance.Net.Objects.Models.Spot;

namespace CoinSoul.Trading.Engine.Cache;

public interface IMarketDataCache
{
    Task<List<Ticker24h>> GetOrFetch24hTickersAsync(CancellationToken ct);
    
    Task<BookTicker?> GetOrFetchBookTickerAsync(string symbol, CancellationToken ct);
    
    Task<KlineData?> GetOrFetchKlinesAsync(
        string symbol, 
        KlineInterval interval, 
        int limit, 
        CancellationToken ct);
    
    Task<BinanceExchangeInfo?> GetOrFetchExchangeInfoAsync(CancellationToken ct);
    
    CacheStats GetStats();
    
    void ClearExpired();
}