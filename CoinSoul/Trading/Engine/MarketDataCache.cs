using System.Collections.Concurrent;
using Binance.Net.Objects.Models.Spot;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

public sealed class MarketDataCache
{
    private readonly ILogger<MarketDataCache> _logger;
    
    private readonly ConcurrentDictionary<string, CachedTickerData> _tickerCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedKlineData> _klineCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CachedBookData> _bookCache = new(StringComparer.OrdinalIgnoreCase);
    
    private DateTime _exchangeInfoCachedAt = DateTime.MinValue;
    private BinanceExchangeInfo? _cachedExchangeInfo;
    private readonly SemaphoreSlim _exchangeInfoLock = new(1, 1);

    public MarketDataCache(ILogger<MarketDataCache> logger)
    {
        _logger = logger;
    }

    public sealed record CachedTickerData(
        Binance24HPrice Data,
        DateTime CachedAt,
        TimeSpan ValidFor);

    public sealed record CachedKlineData(
        List<decimal> Closes,
        DateTime CachedAt,
        TimeSpan ValidFor);

    public sealed record CachedBookData(
        decimal BidPrice,
        decimal AskPrice,
        DateTime CachedAt,
        TimeSpan ValidFor);

    public bool TryGetTicker(string symbol, out Binance24HPrice? ticker)
    {
        if (_tickerCache.TryGetValue(symbol, out var cached))
        {
            if (cached.CachedAt.Add(cached.ValidFor) > DateTime.UtcNow)
            {
                ticker = cached.Data;
                return true;
            }
            
            _tickerCache.TryRemove(symbol, out _);
        }

        ticker = null;
        return false;
    }

    public void SetTicker(string symbol, Binance24HPrice data, TimeSpan validFor)
    {
        _tickerCache[symbol] = new CachedTickerData(data, DateTime.UtcNow, validFor);
    }

    public bool TryGetKlines(string symbol, string intervalKey, out List<decimal>? closes)
    {
        var key = $"{symbol}:{intervalKey}";
        
        if (_klineCache.TryGetValue(key, out var cached))
        {
            if (cached.CachedAt.Add(cached.ValidFor) > DateTime.UtcNow)
            {
                closes = cached.Closes;
                return true;
            }
            
            _klineCache.TryRemove(key, out _);
        }

        closes = null;
        return false;
    }

    public void SetKlines(string symbol, string intervalKey, List<decimal> closes, TimeSpan validFor)
    {
        var key = $"{symbol}:{intervalKey}";
        _klineCache[key] = new CachedKlineData(closes, DateTime.UtcNow, validFor);
    }

    public bool TryGetBook(string symbol, out (decimal Bid, decimal Ask)? book)
    {
        if (_bookCache.TryGetValue(symbol, out var cached))
        {
            if (cached.CachedAt.Add(cached.ValidFor) > DateTime.UtcNow)
            {
                book = (cached.BidPrice, cached.AskPrice);
                return true;
            }
            
            _bookCache.TryRemove(symbol, out _);
        }

        book = null;
        return false;
    }

    public void SetBook(string symbol, decimal bid, decimal ask, TimeSpan validFor)
    {
        _bookCache[symbol] = new CachedBookData(bid, ask, DateTime.UtcNow, validFor);
    }

    public async Task<BinanceExchangeInfo?> GetOrFetchExchangeInfoAsync(
        Func<Task<BinanceExchangeInfo?>> fetchFunc,
        TimeSpan validFor)
    {
        if (_cachedExchangeInfo != null && 
            _exchangeInfoCachedAt.Add(validFor) > DateTime.UtcNow)
        {
            return _cachedExchangeInfo;
        }

        await _exchangeInfoLock.WaitAsync();
        try
        {
            // Double-check after acquiring lock
            if (_cachedExchangeInfo != null && 
                _exchangeInfoCachedAt.Add(validFor) > DateTime.UtcNow)
            {
                return _cachedExchangeInfo;
            }

            var result = await fetchFunc();
            if (result != null)
            {
                _cachedExchangeInfo = result;
                _exchangeInfoCachedAt = DateTime.UtcNow;
            }

            return result;
        }
        finally
        {
            _exchangeInfoLock.Release();
        }
    }

    public void ClearExpiredEntries()
    {
        var now = DateTime.UtcNow;
        
        var expiredTickers = _tickerCache
            .Where(kvp => kvp.Value.CachedAt.Add(kvp.Value.ValidFor) < now)
            .Select(kvp => kvp.Key)
            .ToList();

        var expiredKlines = _klineCache
            .Where(kvp => kvp.Value.CachedAt.Add(kvp.Value.ValidFor) < now)
            .Select(kvp => kvp.Key)
            .ToList();

        var expiredBooks = _bookCache
            .Where(kvp => kvp.Value.CachedAt.Add(kvp.Value.ValidFor) < now)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredTickers)
            _tickerCache.TryRemove(key, out _);

        foreach (var key in expiredKlines)
            _klineCache.TryRemove(key, out _);

        foreach (var key in expiredBooks)
            _bookCache.TryRemove(key, out _);

        if (expiredTickers.Count + expiredKlines.Count + expiredBooks.Count > 0)
        {
            _logger.LogDebug("[CACHE_CLEANUP] Removed: Tickers={T}, Klines={K}, Books={B}",
                expiredTickers.Count, expiredKlines.Count, expiredBooks.Count);
        }
    }
}