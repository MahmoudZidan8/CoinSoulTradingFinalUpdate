using Binance.Net.Interfaces.Clients;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Reconciles database positions with Binance reality
/// Ensures DB stays in sync with actual exchange state
/// </summary>
public sealed class PositionReconciliationService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PositionReconciliationService> _logger;

    public PositionReconciliationService(
        IServiceProvider serviceProvider,
        ILogger<PositionReconciliationService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[RECONCILIATION] Service started");

        // Wait 30 seconds after startup
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ReconcilePositionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RECONCILIATION] Error during cycle");
            }

            // Default 30 seconds, configurable via settings
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
        }

        _logger.LogInformation("[RECONCILIATION] Service stopped");
    }

    private async Task ReconcilePositionsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<CoinSoulDbContext>>();
        var binanceClient = scope.ServiceProvider.GetRequiredService<IBinanceRestClient>();

        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var openPositions = await db.Positions
            .Where(p => p.IsOpen && p.IsActive)
            .ToListAsync(ct);

        if (openPositions.Count == 0)
            return;

        _logger.LogDebug("[RECONCILIATION] Checking {Count} open positions", openPositions.Count);

        var reconciledCount = 0;

        foreach (var pos in openPositions)
        {
            try
            {
                var reconciled = await ReconcileSinglePositionAsync(pos, db, binanceClient, ct);
                if (reconciled)
                    reconciledCount++;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RECONCILIATION] Failed for position {Id} {Symbol}", 
                    pos.Id, pos.Symbol);
            }
        }

        if (reconciledCount > 0)
        {
            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[RECONCILIATION] Closed {Count} positions", reconciledCount);
        }
    }

    private async Task<bool> ReconcileSinglePositionAsync(
        PositionEntity pos,
        CoinSoulDbContext db,
        IBinanceRestClient binanceClient,
        CancellationToken ct)
    {
        // ✅ SKIP DUST POSITIONS - They are terminal and should not be reopened
        if (pos.CloseReason == "DUST_IGNORED")
        {
            _logger.LogDebug("[RECONCILIATION_SKIP] Position {Id} {Symbol} is dust - skipping", 
                pos.Id, pos.Symbol);
            return false;
        }

        // Check 1: OCO order status
        if (pos.OcoOrderId.HasValue)
        {
            var ocoResult = await binanceClient.SpotApi.Trading.GetOcoOrderAsync(
                orderListId: pos.OcoOrderId.Value,
                ct: ct);

            if (ocoResult.Success && ocoResult.Data != null)
            {
                var ocoStatus = ocoResult.Data.ListOrderStatus;
                
                // OCO executed (one leg filled)
                if (ocoStatus == Binance.Net.Enums.ListOrderStatus.Done)
                {
                    _logger.LogInformation("[RECONCILIATION] OCO filled: Position {Id} {Symbol}", 
                        pos.Id, pos.Symbol);

                    // Find which leg filled
                    var orders = ocoResult.Data.Orders;

                    Binance.Net.Enums.OrderStatus? filledOrderStatus = null;
                    decimal? filledOrderPrice = null;
                    long? filledOrderId = null;

                    foreach (var orderId in orders)
                    {
                        var orderResult = await binanceClient.SpotApi.Trading.GetOrderAsync(
                            symbol: orderId.Symbol,
                            orderId: orderId.OrderId,
                            ct: ct);

                        if (orderResult.Success && orderResult.Data != null)
                        {
                            if (orderResult.Data.Status == Binance.Net.Enums.OrderStatus.Filled)
                            {
                                filledOrderStatus = orderResult.Data.Status;
                                filledOrderPrice = orderResult.Data.Price;
                                filledOrderId = orderResult.Data.OrderListId;
                                break;
                            }
                        }
                    }

                    if (filledOrderStatus == Binance.Net.Enums.OrderStatus.Filled)
                    {
                        pos.ExitPrice = filledOrderPrice;
                        pos.SellOrderId = filledOrderId;
                    }

                    await ClosePositionAsync(pos, db, "OCO_FILLED", ct);
                    return true;
                }
            }
        }

        // Check 2: Asset balance (detect manual sells)
        var baseAsset = pos.Symbol.Replace("USDT", "");
        var accountResult = await binanceClient.SpotApi.Account.GetAccountInfoAsync(ct: ct);

        if (accountResult.Success && accountResult.Data != null)
        {
            var balance = accountResult.Data.Balances
                .FirstOrDefault(b => b.Asset == baseAsset);

            // No balance left - position was closed externally
            if (balance == null || balance.Total < pos.Quantity * 0.1m)
            {
                _logger.LogWarning("[RECONCILIATION] External close detected: Position {Id} {Symbol}", 
                    pos.Id, pos.Symbol);

                await ClosePositionAsync(pos, db, "EXTERNAL_CLOSE", ct);
                return true;
            }
        }

        return false;
    }

    private async Task ClosePositionAsync(
        PositionEntity pos,
        CoinSoulDbContext db,
        string reason,
        CancellationToken ct)
    {
        pos.IsOpen = false;
        pos.IsActive = false;
        pos.ExitCompleted = true;
        pos.ClosedAtUtc = DateTime.UtcNow;
        pos.CloseReason = reason;

        // Log event
        db.TradingEvents.Add(new TradingEventEntity
        {
            AtUtc = DateTimeOffset.UtcNow,
            Level = "INFO",
            Type = "RECONCILIATION_CLOSE",
            Symbol = pos.Symbol,
            Message = $"Position {pos.Id} closed by reconciliation: {reason}",
            CorrelationId = pos.Id.ToString()
        });
    }
}