using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Binance.Net.Clients;
using Binance.Net.Interfaces.Clients;
using Binance.Net.ExtensionMethods;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Engine.Cache;
using CryptoExchange.Net.Objects.Sockets;
using CryptoExchange.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Streaming;

public sealed class MarketStreamService : IMarketStreamService
{
    private readonly IBinanceSocketClient _socketClient;
    private readonly ILogger<MarketStreamService> _logger;
    private readonly IClock _clock;
    private readonly MarketStreamOptions _options;

    private readonly ConcurrentDictionary<string, StreamedBookTicker> _bookTickers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, StreamedTicker24h> _tickers24h = new(StringComparer.OrdinalIgnoreCase);

    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);
    private readonly CancellationTokenSource _internalCts = new();

    private UpdateSubscription? _allMarketTickerSubscription;
    private UpdateSubscription? _bookTickerSubscription;

    private DateTime _startedUtc = DateTime.MinValue;
    private DateTime _lastDataReceivedUtc = DateTime.MinValue;
    private int _reconnectCount = 0;
    private bool _isRunning = false;
    private bool _disposed = false;

    // ✅ PATCH 4: Add validation regex (same as other files)
    private static readonly Regex ValidSymbolRegex = new(@"^[A-Z0-9]{2,20}USDT$", RegexOptions.Compiled);

    public bool IsHealthy =>
        _isRunning &&
        _lastDataReceivedUtc > DateTime.MinValue &&
        (DateTime.UtcNow - _lastDataReceivedUtc).TotalSeconds < _options.StaleDataThresholdSeconds;

    public MarketStreamService(
        IBinanceSocketClient socketClient,
        ILogger<MarketStreamService> logger,
        IClock clock,
        IConfiguration configuration)
    {
        _socketClient = socketClient;
        _logger = logger;
        _clock = clock;

        _options = new MarketStreamOptions();
        configuration.GetSection("MarketStream").Bind(_options);
    }

    public async Task StartAsync(CancellationToken ct)
    {
        if (_isRunning)
        {
            _logger.LogWarning("[WS] Already running, skipping start");
            return;
        }

        _startedUtc = _clock.UtcNow;
        _isRunning = true;

        _logger.LogInformation("[WS] Starting MarketStreamService | EnableDebugLogs={Debug}",
            _options.EnableDebugLogs);

        try
        {
            // Subscribe to all market mini tickers (lightweight, all symbols)
            await SubscribeAllMarketTickersAsync(ct);

            _logger.LogInformation("[WS] Connected successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WS] Failed to start streaming");
            _isRunning = false;
            throw;
        }
    }

    public async Task StopAsync(CancellationToken ct)
    {
        if (!_isRunning)
            return;

        _logger.LogInformation("[WS] Stopping MarketStreamService");

        _isRunning = false;
        _internalCts.Cancel();

        try
        {
            if (_allMarketTickerSubscription != null)
            {
                await _allMarketTickerSubscription.CloseAsync();
                _allMarketTickerSubscription = null;
            }

            if (_bookTickerSubscription != null)
            {
                await _bookTickerSubscription.CloseAsync();
                _bookTickerSubscription = null;
            }

            _logger.LogInformation("[WS] Disconnected");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WS] Error during stop");
        }
    }

    /// <summary>
    /// ✅ PATCH 4: Validates symbol format strictly before Binance WebSocket subscription.
    /// </summary>
    private static bool IsValidBinanceSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var trimmed = symbol.Trim().ToUpperInvariant();
        return ValidSymbolRegex.IsMatch(trimmed);
    }

    public async Task SubscribeSymbolsAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        // Materialize once (symbols may be a lazy enumerable)
        var symbolList = symbols?.ToList() ?? new List<string>();

        // ✅ PATCH 4: Final defensive validation before Binance API call
        var validSymbols = symbolList
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Where(IsValidBinanceSymbol)
            // Extra safety: Binance.Net's own validator (catches edge cases / hidden chars)
            .Where(s =>
            {
                try { BinanceExtensionMethods.ValidateBinanceSymbol(s); return true; }
                catch { return false; }
            })
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var invalidCount = symbolList.Count - validSymbols.Length;

        if (invalidCount > 0)
        {
            _logger.LogWarning("[WS_SUB_FILTERED] Filtered {Invalid} invalid symbols before Binance subscription",
                invalidCount);
        }

        if (validSymbols.Length == 0)
        {
            _logger.LogWarning("[WS_SUB_EMPTY] No valid symbols after sanitization - skipping subscription");
            return;
        }

        _logger.LogInformation("[WS_SUB] Subscribing to {Count} validated symbols", validSymbols.Length);

        if (!_isRunning)
        {
            _logger.LogWarning("[WS_SUB] Not running, skipping subscription");
            return;
        }

        // rawSymbols already materialized above

        var acquired = false;
        try
        {
            // ✅ MUST acquire before releasing; releasing without waiting can throw SemaphoreFullException
            await _subscriptionLock.WaitAsync(ct);
            acquired = true;

            // Unsubscribe from previous bookTicker stream
            if (_bookTickerSubscription != null)
            {
                await _bookTickerSubscription.CloseAsync();
                _bookTickerSubscription = null;
            }

            // Subscribe to bookTicker for specific symbols
            var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(
                validSymbols,
                data =>
                {
                    _lastDataReceivedUtc = _clock.UtcNow;

                    var ticker = new StreamedBookTicker(
                        data.Data.Symbol,
                        data.Data.BestBidPrice,
                        data.Data.BestBidQuantity,
                        data.Data.BestAskPrice,
                        data.Data.BestAskQuantity,
                        _clock.UtcNow);

                    _bookTickers[data.Data.Symbol] = ticker;

                    if (_options.EnableDebugLogs)
                    {
                        _logger.LogDebug("[WS_DATA] BookTicker {Symbol} Bid={Bid} Ask={Ask}",
                            data.Data.Symbol, data.Data.BestBidPrice, data.Data.BestAskPrice);
                    }
                },
                ct);

            if (!subscribeResult.Success)
            {
                _logger.LogError("[WS_SUB] Failed to subscribe to bookTickers: {Error}",
                    subscribeResult.Error?.Message ?? "Unknown");
                return;
            }

            _bookTickerSubscription = subscribeResult.Data;

            // Setup reconnection handler
            _bookTickerSubscription.ConnectionLost += async () =>
            {
                _logger.LogWarning("[WS] BookTicker connection lost, attempting reconnect...");
                _reconnectCount++;
                await Task.Delay(_options.ReconnectDelayMs);
            };

            _bookTickerSubscription.ConnectionRestored += (timeOffline) =>
            {
                _logger.LogInformation("[WS] BookTicker connection restored after {Offline}ms", timeOffline);
            };

            _logger.LogInformation("[WS_SUB] Subscribed to {Count} symbols for bookTicker", validSymbols.Length);
        }
        catch (OperationCanceledException)
        {
            // normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WS_SUB] Error subscribing to symbols");
        }
        finally
        {
            if (acquired)
                _subscriptionLock.Release();
        }
    }

    public BookTicker? GetLatestBookTicker(string symbol)
    {
        if (!_bookTickers.TryGetValue(symbol, out var streamed))
            return null;

        // Check if data is fresh
        var age = (_clock.UtcNow - streamed.ReceivedAtUtc).TotalSeconds;
        if (age > _options.StaleDataThresholdSeconds)
            return null;

        return new BookTicker(
            streamed.Symbol,
            streamed.BidPrice,
            streamed.BidQuantity,
            streamed.AskPrice,
            streamed.AskQuantity);
    }

    public Ticker24h? GetLatest24hTicker(string symbol)
    {
        if (!_tickers24h.TryGetValue(symbol, out var streamed))
            return null;

        var age = (_clock.UtcNow - streamed.ReceivedAtUtc).TotalSeconds;
        if (age > _options.StaleDataThresholdSeconds)
            return null;

        return new Ticker24h(
            streamed.Symbol,
            streamed.LastPrice,
            streamed.QuoteVolume,
            streamed.PriceChangePercent);
    }

    public List<Ticker24h> GetAll24hTickers()
    {
        var now = _clock.UtcNow;
        var threshold = TimeSpan.FromSeconds(_options.StaleDataThresholdSeconds);

        return _tickers24h.Values
            .Where(t => (now - t.ReceivedAtUtc) < threshold)
            .Select(t => new Ticker24h(
                t.Symbol,
                t.LastPrice,
                t.QuoteVolume,
                t.PriceChangePercent))
            .ToList();
    }

    public StreamStats GetStats()
    {
        var uptime = _isRunning
            ? _clock.UtcNow - _startedUtc
            : TimeSpan.Zero;

        return new StreamStats(
            _isRunning,
            _bookTickers.Count,
            _bookTickers.Count,
            _tickers24h.Count,
            _lastDataReceivedUtc,
            _reconnectCount,
            uptime);
    }

    private async Task SubscribeAllMarketTickersAsync(CancellationToken ct)
    {
        var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToAllTickerUpdatesAsync(
            data =>
            {
                _lastDataReceivedUtc = _clock.UtcNow;

                foreach (var ticker in data.Data)
                {
                    // Filter for USDT pairs only
                    if (!ticker.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var streamed = new StreamedTicker24h(
                        ticker.Symbol,
                        ticker.LastPrice,
                        ticker.QuoteVolume,
                        ticker.PriceChangePercent,
                        _clock.UtcNow);

                    _tickers24h[ticker.Symbol] = streamed;
                }

                if (_options.EnableDebugLogs)
                {
                    _logger.LogDebug("[WS_DATA] Received {Count} ticker updates",
                        data.Data.Count());
                }
            },
            ct);

        if (!subscribeResult.Success)
        {
            throw new InvalidOperationException(
                $"Failed to subscribe to all market tickers: {subscribeResult.Error?.Message}");
        }

        _allMarketTickerSubscription = subscribeResult.Data;

        // Setup reconnection handlers
        _allMarketTickerSubscription.ConnectionLost += async () =>
        {
            _logger.LogWarning("[WS] AllMarketTicker connection lost, attempting reconnect...");
            _reconnectCount++;
            await Task.Delay(_options.ReconnectDelayMs);
        };

        _allMarketTickerSubscription.ConnectionRestored += (timeOffline) =>
        {
            _logger.LogInformation("[WS] AllMarketTicker connection restored after {Offline}ms", timeOffline);
        };

        _logger.LogInformation("[WS] Subscribed to all market tickers stream");
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _internalCts.Cancel();
        _internalCts.Dispose();
        _subscriptionLock.Dispose();
    }

    private static string NormalizeSymbol(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;

        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.Trim())
        {
            if (char.IsWhiteSpace(ch) || char.IsControl(ch) || ch > 127)
                continue;
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }

    private static bool IsValidSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        return ValidSymbolRegex.IsMatch(symbol);
    }

    private sealed record StreamedBookTicker(
        string Symbol,
        decimal BidPrice,
        decimal BidQuantity,
        decimal AskPrice,
        decimal AskQuantity,
        DateTime ReceivedAtUtc);

    private sealed record StreamedTicker24h(
        string Symbol,
        decimal LastPrice,
        decimal QuoteVolume,
        decimal PriceChangePercent,
        DateTime ReceivedAtUtc);
}

public sealed class MarketStreamOptions
{
    public bool EnableDebugLogs { get; set; } = false;
    public int StaleDataThresholdSeconds { get; set; } = 10;
    public int ReconnectDelayMs { get; set; } = 2000;
    public int SubscriptionRefreshIntervalSeconds { get; set; } = 60;
}
