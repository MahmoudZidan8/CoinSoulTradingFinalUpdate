using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public static class ProfitTargetCalculator
{
    /// <summary>
    /// Calculate TP price for GROSS profit percentage target (e.g. 1.0% gross = ~0.8% net after fees)
    /// </summary>
    public static decimal CalculateTpPriceGross(
        decimal entryPrice,
        decimal grossProfitPct = 1.0m)
    {
        return entryPrice * (1m + (grossProfitPct / 100m));
    }

    /// <summary>
    /// Calculate SL price for GROSS loss percentage (e.g. 1.2% gross loss)
    /// </summary>
    public static decimal CalculateSlPriceGross(
        decimal entryPrice,
        decimal grossLossPct = 1.2m)
    {
        return entryPrice * (1m - (grossLossPct / 100m));
    }

    /// <summary>
    /// Calculate stop-limit price (slightly below stop price to ensure execution)
    /// </summary>
    public static decimal CalculateStopLimitPrice(decimal stopPrice, decimal bufferPct = 0.1m)
    {
        return stopPrice * (1m - (bufferPct / 100m));
    }

    /// <summary>
    /// Floor quantity to step size (exchange precision requirement)
    /// </summary>
    public static decimal FloorToStepSize(decimal quantity, decimal stepSize)
    {
        if (stepSize <= 0) return quantity;
        return Math.Floor(quantity / stepSize) * stepSize;
    }

    /// <summary>
    /// Round price to tick size (exchange precision requirement)
    /// </summary>
    public static decimal RoundToTickSize(decimal price, decimal tickSize)
    {
        if (tickSize <= 0) return price;
        return Math.Round(price / tickSize, MidpointRounding.ToZero) * tickSize;
    }

    /// <summary>
    /// Apply quantity buffer to prevent "insufficient balance" errors
    /// </summary>
    public static decimal ApplyQtyBuffer(decimal quantity, decimal bufferPct = 0.002m)
    {
        return quantity * (1m - bufferPct);
    }

    /// <summary>
    /// Validate if quantity meets minimum requirements
    /// </summary>
    public static (bool Valid, string Reason) ValidateQuantity(
        decimal quantity,
        decimal minQty,
        decimal price,
        decimal minNotional)
    {
        if (quantity < minQty)
        {
            return (false, $"Quantity {quantity:0.########} below MinQty {minQty:0.########}");
        }

        var notional = quantity * price;
        if (notional < minNotional)
        {
            return (false, $"Notional {notional:0.00} below MinNotional {minNotional:0.00}");
        }

        return (true, "OK");
    }
}