using CryptoExchange.Net.CommonObjects;

namespace CoinSoul.Trading.Core;

/// <summary>
/// Trading executor interface - SINGLE SOURCE OF TRUTH
/// </summary>
public interface ITradeExecutor
{
    // Buy operations
    Task<LiveBuyResult> MarketBuyAsync(string symbol, decimal usdtAmount, CancellationToken ct);
    Task<LiveBuyResult> LimitBuyMakerAsync(string symbol, decimal usdtAmount, decimal limitPrice, CancellationToken ct, int timeoutSeconds = 5);

    // Sell operations
    Task<LiveSellResult> MarketSellAsync(string symbol, decimal quantity, CancellationToken ct);
    Task<LiveOcoResult> PlaceOcoSellAsync(
        string symbol,
        decimal quantity,
        decimal takeProfitPrice,
        decimal stopPrice,
        decimal stopLimitPrice,
        CancellationToken ct);
    Task<TradeResult> PlaceLimitSellAsync(
        string symbol, 
        decimal quantity, 
        decimal price, 
        CancellationToken ct);
    Task<TradeResult> PlaceStopLossLimitAsync(
        string symbol, 
        decimal quantity, 
        decimal stopPrice, 
        decimal limitPrice, 
        CancellationToken ct);
    
    // Balance queries
    Task<decimal> GetFreeBaseAssetAsync(string symbol, CancellationToken ct);
    Task<(decimal Free, decimal Locked, decimal Total)> GetBaseAssetBalanceAsync(string symbol, CancellationToken ct);
    
    // Rules and orders
    Task<SymbolTradingRules?> GetRulesAsync(string symbol, CancellationToken ct);
    Task<bool> HasAnyOpenOrdersAsync(string symbol, CancellationToken ct);
    Task<bool> CancelAllOpenOrdersAsync(string symbol, CancellationToken ct);
}

// ===== Result Types - SINGLE DEFINITIONS =====

public sealed record LiveBuyResult(
    bool Success,
    long? OrderId,
    decimal ExecutedQty,
    decimal AvgPrice,
    decimal QuoteUsed,
    string? Error);

public sealed record LiveSellResult(
    bool Success,
    long? OrderId,
    decimal ExecutedQty,
    decimal AvgPrice,
    decimal QuoteReceived,
    string? Error);

public sealed record LiveOcoResult(
    bool Success,
    long? OrderListId,
    string? Error);

public sealed record SymbolTradingRules(
    decimal StepSize,
    decimal MinQty,
    decimal TickSize,
    decimal MinNotional);

public sealed record TradeResult(
    bool Success,
    string Message,
    decimal? ExecutedPrice,
    decimal? Quantity,
    long? OrderId);