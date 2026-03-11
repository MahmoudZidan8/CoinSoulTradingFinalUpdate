using Binance.Net.Interfaces.Clients;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Background service that syncs Binance account trades to the local database
/// Runs every 2 minutes for Spot USDT pairs
/// </summary>
public sealed class BinanceTradeSyncService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IBinanceRestClient _binanceClient;
    private readonly ILogger<BinanceTradeSyncService> _logger;

    private List<string> _cachedUsdtSymbols = new();
    private DateTime _symbolsCacheExpiry = DateTime.MinValue;
    private const int SyncIntervalMinutes = 10;
    private const int SymbolCacheHours = 6;
    private const int LookbackDays = 3;

    public BinanceTradeSyncService(
        IServiceProvider serviceProvider,
        IBinanceRestClient binanceClient,
        ILogger<BinanceTradeSyncService> logger)
    {
        _serviceProvider = serviceProvider;
        _binanceClient = binanceClient;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("BinanceTradeSyncService started");

        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncTradesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during trade sync");
            }

            await Task.Delay(TimeSpan.FromMinutes(SyncIntervalMinutes), stoppingToken);
        }
    }

    private async Task SyncTradesAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting trade sync cycle");

        using var scope = _serviceProvider.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CoinSoulDbContext>>();
        var tradeWriter = scope.ServiceProvider.GetRequiredService<IAccountTradeWriter>();

        // بدل ما نلف على كل USDT pairs
        // نجيب الرموز اللي حصل عليها تداول فعلاً خلال آخر 30 يوم
        var activeSymbols = await GetActiveSpotSymbolsAsync(ct);

        if (activeSymbols.Count == 0)
        {
            _logger.LogInformation("No active symbols found.");
            return;
        }

        var totalSynced = 0;

        foreach (var symbol in activeSymbols)
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                var synced = await SyncSymbolTradesAsync(
                    symbol,
                    DateTime.UtcNow.AddDays(-30),
                    dbFactory,
                    tradeWriter,
                    ct);

                totalSynced += synced;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed syncing {Symbol}", symbol);
            }

            await Task.Delay(100, ct); // Rate limit protection
        }

        _logger.LogInformation("Trade sync finished. Total new trades: {Count}", totalSynced);
    }

    private async Task<List<string>> GetActiveSpotSymbolsAsync(CancellationToken ct)
    {
        var result = await _binanceClient.SpotApi.Account.GetAccountInfoAsync(ct: ct);

        if (!result.Success || result.Data == null)
            return new List<string>();

        var validUsdt = await GetUsdtSymbolsAsync(ct);
        var validSet = validUsdt.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var symbols = result.Data.Balances
            .Where(b => b.Total > 0 && !string.Equals(b.Asset, "USDT", StringComparison.OrdinalIgnoreCase))
            .Select(b => b.Asset + "USDT")
            .Where(s => validSet.Contains(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return symbols;
    }
    private async Task<int> SyncSymbolTradesAsync(
        string symbol,
        DateTime startTime,
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        IAccountTradeWriter tradeWriter,
        CancellationToken ct)
    {
        // Fetch trades from Binance
        var tradesResult = await _binanceClient.SpotApi.Trading.GetUserTradesAsync(
            symbol: symbol,
            startTime: startTime,
            limit: 1000,
            ct: ct);

        if (!tradesResult.Success || tradesResult.Data == null)
        {
            _logger.LogDebug("No trades returned for {Symbol}: {Error}", 
                symbol, 
                tradesResult.Error?.Message ?? "Unknown");
            return 0;
        }

        var binanceTrades = tradesResult.Data.ToList();
        if (binanceTrades.Count == 0)
            return 0;

        // Get existing trade IDs from database
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var tradeIds = binanceTrades.Select(t => t.Id).ToList();
        var existingIds = await db.AccountTrades
            .Where(t => tradeIds.Contains(t.TradeId))
            .Select(t => t.TradeId)
            .ToListAsync(ct);

        var existingSet = new HashSet<long>(existingIds);

        // Filter to only new trades
        var newTrades = binanceTrades
            .Where(t => !existingSet.Contains(t.Id))
            .ToList();

        if (newTrades.Count == 0)
            return 0;

        // Convert to AccountTradeEntity
        var entities = newTrades.Select(trade => new AccountTradeEntity
        {
            TradeId = trade.Id,
            Symbol = symbol,
            Side = trade.IsBuyer ? "BUY" : "SELL",
            Price = trade.Price,
            Quantity = trade.Quantity,
            QuoteQty = trade.QuoteQuantity,
            Commission = trade.Fee,
            CommissionAsset = trade.FeeAsset,
            IsMaker = trade.IsMaker,
            // ✅ CRITICAL: Ensure trade.Timestamp is in UTC
            // Binance API returns UTC timestamps, but explicitly convert to be safe
            TradeTimeUtc = trade.Timestamp.Kind == DateTimeKind.Utc 
                ? trade.Timestamp 
                : trade.Timestamp.ToUniversalTime(),
            Source = "SYNC",
            OrderId = trade.OrderId
        }).ToList();

        // Save to database using batch insert
        var savedCount = await tradeWriter.SaveBatchAsync(entities, ct);

        return savedCount;
    }

    private async Task<List<string>> GetUsdtSymbolsAsync(CancellationToken ct)
    {
        // Return cached symbols if still valid
        if (_cachedUsdtSymbols.Count > 0 && DateTime.UtcNow < _symbolsCacheExpiry)
        {
            return _cachedUsdtSymbols;
        }

        _logger.LogInformation("Refreshing USDT symbols cache");

        try
        {
            // Fetch exchange info
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync(ct);

            if (!exchangeInfoResult.Success || exchangeInfoResult.Data == null)
            {
                _logger.LogError("Failed to fetch exchange info: {Error}", 
                    exchangeInfoResult.Error?.Message ?? "Unknown");
                return _cachedUsdtSymbols; // Return old cache if available
            }

            // Filter for USDT spot pairs that are trading
            var usdtSymbols = exchangeInfoResult.Data.Symbols
                .Where(s => 
                    s.QuoteAsset == "USDT" && 
                    s.Status == Binance.Net.Enums.SymbolStatus.Trading &&
                    !s.Name.Contains("UP") &&
                    !s.Name.Contains("DOWN") &&
                    !s.Name.Contains("BULL") &&
                    !s.Name.Contains("BEAR"))
                .Select(s => s.Name)
                .OrderBy(s => s)
                .ToList();

            _cachedUsdtSymbols = usdtSymbols;
            _symbolsCacheExpiry = DateTime.UtcNow.AddHours(SymbolCacheHours);

            _logger.LogInformation("Cached {Count} USDT symbols (expires in {Hours}h)", 
                usdtSymbols.Count, 
                SymbolCacheHours);

            return _cachedUsdtSymbols;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching USDT symbols");
            return _cachedUsdtSymbols; // Return old cache if available
        }
    }
}