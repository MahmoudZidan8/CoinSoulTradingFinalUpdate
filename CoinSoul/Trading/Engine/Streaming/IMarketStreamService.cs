using CoinSoul.Trading.Engine.Cache;

namespace CoinSoul.Trading.Engine.Streaming;

/// <summary>
/// WebSocket streaming service for real-time market data
/// Provides bookTicker and 24hr ticker streams with automatic reconnection
/// </summary>
public interface IMarketStreamService
{
    /// <summary>
    /// Start streaming service and connect to Binance WebSocket
    /// </summary>
    Task StartAsync(CancellationToken ct);

    /// <summary>
    /// Stop streaming and disconnect
    /// </summary>
    Task StopAsync(CancellationToken ct);

    /// <summary>
    /// Subscribe to specific symbols for real-time bookTicker updates
    /// Replaces previous subscriptions
    /// </summary>
    Task SubscribeSymbolsAsync(IEnumerable<string> symbols, CancellationToken ct);

    /// <summary>
    /// Get latest bookTicker from stream (fast, in-memory)
    /// Returns null if symbol not subscribed or data stale
    /// </summary>
    BookTicker? GetLatestBookTicker(string symbol);

    /// <summary>
    /// Get latest 24hr ticker from stream (fast, in-memory)
    /// Returns null if not available or stale
    /// </summary>
    Ticker24h? GetLatest24hTicker(string symbol);

    /// <summary>
    /// Get all 24hr tickers from stream
    /// </summary>
    List<Ticker24h> GetAll24hTickers();

    /// <summary>
    /// Get current streaming statistics
    /// </summary>
    StreamStats GetStats();

    /// <summary>
    /// Check if streaming is healthy (connected and receiving data)
    /// </summary>
    bool IsHealthy { get; }
}

public sealed record StreamStats(
    bool Connected,
    int SubscribedSymbolCount,
    int BookTickerCount,
    int Ticker24hCount,
    DateTime LastDataReceivedUtc,
    int ReconnectCount,
    TimeSpan Uptime);