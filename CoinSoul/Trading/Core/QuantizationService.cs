namespace CoinSoul.Trading.Core;

/// <summary>
/// Centralized service for price/quantity rounding and validation
/// Ensures all orders meet Binance symbol trading rules
/// </summary>
public sealed class QuantizationService
{
    /// <summary>
    /// Rounds price to the nearest valid tick size
    /// </summary>
    public static decimal RoundPriceToTick(decimal price, decimal tickSize)
    {
        if (tickSize <= 0) return price;
        return Math.Round(price / tickSize, 0, MidpointRounding.ToZero) * tickSize;
    }

    /// <summary>
    /// Floors quantity to valid step size
    /// </summary>
    public static decimal RoundQtyToStep(decimal quantity, decimal stepSize)
    {
        if (stepSize <= 0) return quantity;
        return Math.Floor(quantity / stepSize) * stepSize;
    }

    /// <summary>
    /// Applies quantity buffer (reduces qty by buffer %)
    /// </summary>
    public static decimal ApplyQtyBuffer(decimal quantity, decimal bufferPct)
    {
        if (bufferPct <= 0) return quantity;
        return quantity * (1m - bufferPct / 100m);
    }

    /// <summary>
    /// Floors quote quantity (USDT spend) to a safe precision accepted by Binance.
    /// Spot quoteOrderQty is typically safe at 2 decimals for USDT pairs.
    /// </summary>
    public static decimal RoundQuoteToPrecision(decimal quoteQuantity, int decimals = 2)
    {
        if (quoteQuantity <= 0) return 0;
        if (decimals < 0) decimals = 0;
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Floor(quoteQuantity * factor) / factor;
    }


    /// <summary>
    /// Returns exchange-safe decimal precision implied by a step/tick size.
    /// For example 0.001 => 3, 1 => 0.
    /// </summary>
    public static int GetPrecisionFromStep(decimal step)
    {
        if (step <= 0) return 8;
        step = decimal.Abs(step);
        var scale = (decimal.GetBits(step)[3] >> 16) & 0x7F;
        return scale;
    }

    /// <summary>
    /// Trims a decimal down to the exact number of decimals accepted by the exchange.
    /// </summary>
    public static decimal TrimToPrecision(decimal value, int decimals)
    {
        if (decimals < 0) decimals = 0;
        if (decimals > 18) decimals = 18;
        var factor = (decimal)Math.Pow(10, decimals);
        return Math.Truncate(value * factor) / factor;
    }

    /// <summary>
    /// Floors price to tick and trims the resulting decimal to the tick precision.
    /// </summary>
    public static decimal NormalizePriceForExchange(decimal price, decimal tickSize)
    {
        var floored = RoundPriceToTick(price, tickSize);
        return TrimToPrecision(floored, GetPrecisionFromStep(tickSize));
    }

    /// <summary>
    /// Floors quantity to step and trims the resulting decimal to the step precision.
    /// </summary>
    public static decimal NormalizeQtyForExchange(decimal qty, decimal stepSize)
    {
        var floored = RoundQtyToStep(qty, stepSize);
        return TrimToPrecision(floored, GetPrecisionFromStep(stepSize));
    }

    /// <summary>
    /// Validates minimum notional value
    /// </summary>
    public static (bool Valid, string Reason) ValidateMinNotional(
        decimal price,
        decimal quantity,
        decimal minNotional)
    {
        if (minNotional <= 0)
            return (true, "");

        var notional = price * quantity;
        if (notional < minNotional)
            return (false, $"Notional ${notional:N2} below MinNotional ${minNotional:N2}");

        return (true, "");
    }

    /// <summary>
    /// Validates minimum quantity
    /// </summary>
    public static (bool Valid, string Reason) ValidateMinQty(
        decimal quantity,
        decimal minQty)
    {
        if (quantity < minQty)
            return (false, $"Qty {quantity:0.########} below MinQty {minQty:0.########}");

        return (true, "");
    }

    /// <summary>
    /// Complete quantity validation and rounding
    /// </summary>
    public static (bool Valid, decimal RoundedQty, string Reason) ValidateAndRoundQuantity(
        decimal rawQuantity,
        decimal stepSize,
        decimal minQty,
        decimal qtyBufferPct = 0)
    {
        // Apply buffer
        var buffered = ApplyQtyBuffer(rawQuantity, qtyBufferPct);

        // Round to step
        var rounded = RoundQtyToStep(buffered, stepSize);

        // Validate min qty
        var qtyCheck = ValidateMinQty(rounded, minQty);
        if (!qtyCheck.Valid)
            return (false, 0, qtyCheck.Reason);

        return (true, rounded, "");
    }

    /// <summary>
    /// Complete order validation (qty + notional)
    /// </summary>
    public static (bool Valid, decimal FinalQty, string Reason) ValidateOrder(
        decimal rawQuantity,
        decimal price,
        SymbolTradingRules rules,
        decimal qtyBufferPct = 0)
    {
        // Validate and round quantity
        var qtyResult = ValidateAndRoundQuantity(
            rawQuantity,
            rules.StepSize,
            rules.MinQty,
            qtyBufferPct);

        if (!qtyResult.Valid)
            return (false, 0, qtyResult.Reason);

        // Validate notional
        var notionalCheck = ValidateMinNotional(
            price,
            qtyResult.RoundedQty,
            rules.MinNotional);

        if (!notionalCheck.Valid)
            return (false, 0, notionalCheck.Reason);

        return (true, qtyResult.RoundedQty, "");
    }
}