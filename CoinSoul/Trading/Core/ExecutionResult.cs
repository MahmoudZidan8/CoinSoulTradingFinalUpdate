namespace CoinSoul.Trading.Core;

/// <summary>
/// Unified execution result for all trade operations
/// Replaces LiveBuyResult/LiveSellResult/HybridEntryResult
/// </summary>
public sealed record ExecutionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public long? OrderId { get; init; }
    public decimal ExecutedQty { get; init; }
    public decimal AvgPrice { get; init; }
    public decimal QuoteAmount { get; init; } // Spent (buy) or Received (sell)
    public string? RawStatus { get; init; }
    public string ExecutionMethod { get; init; } = ""; // LIMIT_MAKER, MARKET, OCO, etc.

    public static ExecutionResult Failure(string error) =>
        new() { Success = false, Error = error };

    public static ExecutionResult SuccessBuy(
        long orderId,
        decimal qty,
        decimal price,
        decimal quoteSpent,
        string method = "MARKET") =>
        new()
        {
            Success = true,
            OrderId = orderId,
            ExecutedQty = qty,
            AvgPrice = price,
            QuoteAmount = quoteSpent,
            ExecutionMethod = method
        };

    public static ExecutionResult SuccessSell(
        long orderId,
        decimal qty,
        decimal price,
        decimal quoteReceived,
        string method = "MARKET") =>
        new()
        {
            Success = true,
            OrderId = orderId,
            ExecutedQty = qty,
            AvgPrice = price,
            QuoteAmount = quoteReceived,
            ExecutionMethod = method
        };

    // Conversion from LiveBuyResult
    public static ExecutionResult FromBuyResult(LiveBuyResult result, string method = "MARKET") =>
        result.Success
            ? SuccessBuy(result.OrderId ?? 0, result.ExecutedQty, result.AvgPrice, result.QuoteUsed, method)
            : Failure(result.Error ?? "Unknown buy error");

    // Conversion from LiveSellResult
    public static ExecutionResult FromSellResult(LiveSellResult result, string method = "MARKET") =>
        result.Success
            ? SuccessSell(result.OrderId ?? 0, result.ExecutedQty, result.AvgPrice, result.QuoteReceived, method)
            : Failure(result.Error ?? "Unknown sell error");
}