using CoinSoul.Trading.Engine.Cache;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace CoinSoul.Trading.Engine.Streaming;

/// <summary>
/// ✅ PRODUCTION HOTFIX: Validates symbols before WebSocket subscription.
/// Prevents invalid symbols (e.g., ????USDT) from causing subscription errors.
/// </summary>
public sealed class StreamSubscriptionManager : BackgroundService
{
    private readonly IMarketStreamService _streamService;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<StreamSubscriptionManager> _logger;
    private readonly bool _useWebSocketMarketData;
    private readonly int _refreshIntervalSeconds;
    private readonly int _maxSubscribedSymbols;

    // ✅ PATCH 3: Strict symbol validation (same as SymbolQueueManager)
    private static readonly Regex ValidSymbolRegex = new(@"^[A-Z0-9]{2,20}USDT$", RegexOptions.Compiled);

    public StreamSubscriptionManager(
        IMarketStreamService streamService,
        IMarketDataCache cache,
        ILogger<StreamSubscriptionManager> logger,
        IConfiguration configuration)
    {
        _streamService = streamService;
        _cache = cache;
        _logger = logger;
        
        _useWebSocketMarketData = configuration.GetValue<bool>("UseWebSocketMarketData", false);
        _refreshIntervalSeconds = configuration.GetValue<int>("MarketStream:SubscriptionRefreshIntervalSeconds", 60);
        _maxSubscribedSymbols = configuration.GetValue<int>("MarketStream:MaxSubscribedSymbols", 50);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_useWebSocketMarketData)
        {
            _logger.LogInformation("[WS_MGR] WebSocket streaming disabled via config");
            return;
        }

        _logger.LogInformation("[WS_MGR] Starting StreamSubscriptionManager | RefreshInterval={Interval}s",
            _refreshIntervalSeconds);

        try
        {
            await _streamService.StartAsync(stoppingToken);
            
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RefreshSubscriptionsAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[WS_MGR] Error refreshing subscriptions");
                }

                await Task.Delay(TimeSpan.FromSeconds(_refreshIntervalSeconds), stoppingToken);
            }
        }
        finally
        {
            await _streamService.StopAsync(CancellationToken.None);
            _logger.LogInformation("[WS_MGR] StreamSubscriptionManager stopped");
        }
    }

    /// <summary>
    /// ✅ PATCH 3: Validates symbol format strictly (^[A-Z0-9]{2,20}USDT$).
    /// </summary>
    private static bool IsValidBinanceSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var trimmed = symbol.Trim().ToUpperInvariant();
        return ValidSymbolRegex.IsMatch(trimmed);
    }

    private async Task RefreshSubscriptionsAsync(CancellationToken ct)
    {
        var allTickers = _streamService.GetAll24hTickers();
        
        if (allTickers.Count == 0)
        {
            _logger.LogWarning("[WS_MGR] No tickers available for subscription refresh");
            return;
        }

        var topSymbols = allTickers
            .Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.QuoteVolume)
            .Take(_maxSubscribedSymbols)
            .Select(t => t.Symbol.Trim().ToUpperInvariant())
            .ToList();

        if (topSymbols.Count == 0)
        {
            _logger.LogWarning("[WS_MGR] No valid symbols for subscription");
            return;
        }

        // ✅ PATCH 3: Filter to valid symbols only
        var validSymbols = topSymbols
            .Where(IsValidBinanceSymbol)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var invalidSymbols = topSymbols
            .Where(s => !IsValidBinanceSymbol(s))
            .Take(10) // Limit to avoid log spam
            .ToList();

        var rejectedCount = topSymbols.Count - validSymbols.Length;

        if (rejectedCount > 0)
        {
            _logger.LogWarning("[WS_MGR_REJECTED] Rejected {Rejected}/{Total} invalid symbols before subscribe",
                rejectedCount, topSymbols.Count);
            
            foreach (var invalid in invalidSymbols)
            {
                _logger.LogWarning("[WS_MGR_INVALID] Rejected symbol: \"{Symbol}\"", invalid);
            }
        }

        if (validSymbols.Length == 0)
        {
            _logger.LogError("[WS_MGR_BLOCKED] All symbols rejected - cannot subscribe");
            return;
        }

        _logger.LogInformation("[WS_MGR] Subscribing to {Valid} symbols (rejected {Rejected} invalid)",
            validSymbols.Length, rejectedCount);

        await _streamService.SubscribeSymbolsAsync(validSymbols, ct);

        var stats = _streamService.GetStats();
        _logger.LogInformation(
            "[WS_MGR] Subscription refresh complete | " +
            "Subscribed={Sub}, BookTickers={Book}, Tickers24h={Tickers}, Healthy={Healthy}",
            stats.SubscribedSymbolCount,
            stats.BookTickerCount,
            stats.Ticker24hCount,
            _streamService.IsHealthy);
    }
}