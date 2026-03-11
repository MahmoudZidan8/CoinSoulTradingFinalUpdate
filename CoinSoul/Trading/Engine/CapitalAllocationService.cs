namespace CoinSoul.Trading.Engine;

public sealed class CapitalAllocationService
{
    /// <summary>
    /// Computes order quantity based on capital allocation and symbol rules
    /// </summary>
    public decimal ComputeOrderQuantity(
        decimal capitalPerTrade,
        decimal currentPrice,
        decimal stepSize,
        decimal minQty,
        decimal minNotional)
    {
        if (currentPrice <= 0)
            return 0;

        if (stepSize <= 0)
            return 0;

        var qtyRaw = capitalPerTrade / currentPrice;

        var qtyNormalized = Math.Floor(qtyRaw / stepSize) * stepSize;

        if (qtyNormalized < minQty)
            return 0;

        var notional = qtyNormalized * currentPrice;
        
        if (minNotional > 0 && notional < minNotional)
            return 0;

        return qtyNormalized;
    }
}