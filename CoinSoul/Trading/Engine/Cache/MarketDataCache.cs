using System.Collections.Concurrent;
using System.Threading;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models.Spot;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Engine.Streaming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CoinSoul.Trading.Engine.Cache;

public sealed class MarketDataCache : IMarketDataCache
{
    private readonly IBinanceRestClient _binance;
    private readonly IMarketStreamService? _streamService;
    private readonly ILogger<MarketDataCache> _logger;
    private readonly IClock _clock;
    private readonly MarketDataCacheOptions _options;
    private readonly bool _useWebSocketMarketData;

    private readonly ConcurrentDictionary<string, CachedEntry<BookTicker>> _bookTickers = new();
    private readonly ConcurrentDictionary<string, CachedEntry<KlineData>> _klines = new();
    private CachedEntry<List<Ticker24h>>? _allTickers;
    private CachedEntry<BinanceExchangeInfo>? _exchangeInfo;
    // Prevent API stampede when many parallel tasks request exchangeInfo at startup
    private readonly SemaphoreSlim _exchangeInfoGate = new(1, 1);

    private int _cacheHits = 0;
    private int _cacheMisses = 0;
    private int _streamHits = 0;
    private int _restFallbacks = 0;

    public MarketDataCache(
        IBinanceRestClient binance,
        ILogger<MarketDataCache> logger,
        IClock clock,
        IOptions<MarketDataCacheOptions> options,
        IConfiguration configuration,
        IMarketStreamService? streamService = null) // Optional for backward compatibility
    {
        _binance = binance;
        _streamService = streamService;
        _logger = logger;
        _clock = clock;
        _options = options.Value;
        _useWebSocketMarketData = configuration.GetValue<bool>("UseWebSocketMarketData", false);

        if (_useWebSocketMarketData && _streamService == null)
        {
            _logger.LogWarning("[CACHE] WebSocket enabled but no stream service injected, using REST only");
            _useWebSocketMarketData = false;
        }

        if (_useWebSocketMarketData)
        {
            _logger.LogInformation("[CACHE] WebSocket-first cache mode enabled");
        }
    }

    public async Task<List<Ticker24h>> GetOrFetch24hTickersAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // ✅ TRY STREAM FIRST
        if (_useWebSocketMarketData && _streamService != null && _streamService.IsHealthy)
        {
            var streamTickers = _streamService.GetAll24hTickers();
            if (streamTickers.Count > 0)
            {
                _streamHits++;
                
                if (_options.EnableCacheLogging)
                {
                    _logger.LogDebug("[CACHE_STREAM_HIT] 24h tickers from WebSocket | Count={Count}",
                        streamTickers.Count);
                }

                return streamTickers;
            }
        }

        // ✅ FALLBACK TO CACHED REST DATA
        if (_allTickers != null && !_allTickers.IsExpired())
        {
            _cacheHits++;
            return _allTickers.Data;
        }

        // ✅ FALLBACK TO REST FETCH
        _cacheMisses++;
        _restFallbacks++;

        if (_options.EnableCacheLogging)
        {
            _logger.LogDebug("[CACHE_REST_FETCH] Fetching 24h tickers from REST API");
        }

        var response = await _binance.SpotApi.ExchangeData.GetTickersAsync(ct);
        
        if (!response.Success || response.Data == null)
        {
            _logger.LogError("[CACHE_ERROR] Failed to fetch 24h tickers: {Error}",
                response.Error?.Message ?? "Unknown");
            return _allTickers?.Data ?? new List<Ticker24h>();
        }

        var tickers = response.Data
            .Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            .Select(t => new Ticker24h(
                t.Symbol,
                t.LastPrice,
                t.QuoteVolume,
                t.PriceChangePercent))
            .ToList();

        _allTickers = new CachedEntry<List<Ticker24h>>(tickers, _options.AllTickersTtlMs)
        {
            CachedAtUtc = now
        };
        return tickers;
    }

    public async Task<BookTicker?> GetOrFetchBookTickerAsync(string symbol, CancellationToken ct)
    {
        var now = _clock.UtcNow;

        // ✅ TRY STREAM FIRST
        if (_useWebSocketMarketData && _streamService != null && _streamService.IsHealthy)
        {
            var streamBook = _streamService.GetLatestBookTicker(symbol);
            if (streamBook != null)
            {
                _streamHits++;
                
                if (_options.EnableCacheLogging)
                {
                    _logger.LogDebug("[CACHE_STREAM_HIT] BookTicker {Symbol} from WebSocket",
                        symbol);
                }

                return streamBook;
            }
        }

        // ✅ FALLBACK TO CACHED REST DATA
        if (_bookTickers.TryGetValue(symbol, out var cached) && 
            !cached.IsExpired())
        {
            _cacheHits++;
            return cached.Data;
        }

        // ✅ FALLBACK TO REST FETCH
        _cacheMisses++;
        _restFallbacks++;

        if (_options.EnableCacheLogging)
        {
            _logger.LogDebug("[CACHE_REST_FETCH] Fetching BookTicker {Symbol} from REST API",
                symbol);
        }

        var response = await _binance.SpotApi.ExchangeData.GetBookPriceAsync(symbol, ct);
        
        if (!response.Success || response.Data == null)
        {
            _logger.LogWarning("[CACHE_ERROR] Failed to fetch BookTicker {Symbol}: {Error}",
                symbol, response.Error?.Message ?? "Unknown");
            return cached?.Data;
        }

        var bookTicker = new BookTicker(
            symbol,
            response.Data.BestBidPrice,
            response.Data.BestBidQuantity,
            response.Data.BestAskPrice,
            response.Data.BestAskQuantity);

        _bookTickers[symbol] = new CachedEntry<BookTicker>(bookTicker, _options.BookTtlMs)
        {
            CachedAtUtc = now
        };
        return bookTicker;
    }

    public async Task<KlineData?> GetOrFetchKlinesAsync(
        string symbol,
        KlineInterval interval,
        int limit,
        CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var key = $"{symbol}_{interval}_{limit}";

        // ✅ CHECK CACHE (Klines not streamed, only cached)
        if (_klines.TryGetValue(key, out var cached) && 
            !cached.IsExpired())
        {
            _cacheHits++;
            return cached.Data;
        }

        // ✅ FETCH FROM REST
        _cacheMisses++;

        if (_options.EnableCacheLogging)
        {
            _logger.LogDebug("[CACHE_FETCH] Klines {Symbol} {Interval} limit={Limit}",
                symbol, interval, limit);
        }

        var response = await _binance.SpotApi.ExchangeData.GetKlinesAsync(
            symbol, interval, limit: limit, ct: ct);

        if (!response.Success || response.Data == null)
        {
            _logger.LogWarning("[CACHE_ERROR] Failed to fetch klines {Symbol}: {Error}",
                symbol, response.Error?.Message ?? "Unknown");
            return cached?.Data;
        }

        var klines = response.Data.ToList();
        var closes = klines.Select(k => k.ClosePrice).ToList();
        var highs = klines.Select(k => k.HighPrice).ToList();
        var lows = klines.Select(k => k.LowPrice).ToList();

        var klineData = new KlineData(symbol, interval.ToString(), closes, highs, lows);
        _klines[key] = new CachedEntry<KlineData>(klineData, _options.KlinesTtlMs)
        {
            CachedAtUtc = now
        };

        return klineData;
    }

    public async Task<BinanceExchangeInfo?> GetOrFetchExchangeInfoAsync(CancellationToken ct)
    {
        var now = _clock.UtcNow;

        if (_exchangeInfo != null && 
            !_exchangeInfo.IsExpired())
        {
            _cacheHits++;
            return _exchangeInfo.Data;
        }

        _cacheMisses++;

        await _exchangeInfoGate.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the gate
            if (_exchangeInfo != null && !_exchangeInfo.IsExpired())
            {
                _cacheHits++;
                return _exchangeInfo.Data;
            }

            var response = await _binance.SpotApi.ExchangeData.GetExchangeInfoAsync(ct);

            if (!response.Success || response.Data == null)
            {
                _logger.LogError("[CACHE_ERROR] Failed to fetch exchange info: {Error}",
                    response.Error?.Message ?? "Unknown");
                return _exchangeInfo?.Data;
            }

            _exchangeInfo = new CachedEntry<BinanceExchangeInfo>(response.Data, _options.ExchangeInfoTtlMs)
            {
                CachedAtUtc = now
            };
            return response.Data;
        }
        finally
        {
            _exchangeInfoGate.Release();
        }
    }

    public CacheStats GetStats()
    {
        var totalRequests = _cacheHits + _cacheMisses;
        var hitRate = totalRequests > 0 
            ? (double)_cacheHits / totalRequests * 100 
            : 0;

        var streamRate = totalRequests > 0
            ? (double)_streamHits / totalRequests * 100
            : 0;

        return new CacheStats(
            _cacheHits,
            _cacheMisses,
            hitRate,
            _bookTickers.Count,
            _klines.Count,
            _streamHits,
            _restFallbacks,
            streamRate);
    }

    public void ClearExpired()
    {
        var now = _clock.UtcNow;
        var maxStaleMs = _options.MaxStalenessMs;

        // Clear expired book tickers
        var expiredBooks = _bookTickers
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredBooks)
            _bookTickers.TryRemove(key, out _);

        // Clear expired klines
        var expiredKlines = _klines
            .Where(kvp => kvp.Value.IsExpired())
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKlines)
            _klines.TryRemove(key, out _);

        if (expiredBooks.Count > 0 || expiredKlines.Count > 0)
        {
            _logger.LogDebug("[CACHE_CLEANUP] Removed {Books} book tickers, {Klines} klines",
                expiredBooks.Count, expiredKlines.Count);
        }
    }
}