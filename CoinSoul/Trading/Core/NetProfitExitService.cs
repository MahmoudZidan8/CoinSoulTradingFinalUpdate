using CoinSoul.Entities;

namespace CoinSoul.Trading.Core;

/// <summary>
/// Calculates take-profit price from net USD target
/// Accounts for fees, slippage, and spread
/// </summary>
public sealed class NetProfitExitService
{
    /// <summary>
    /// Computes TP price required to achieve a target net USD after Binance fees and small execution buffers.
    /// Percent-like settings such as SlippageBufferPct / SpreadBufferPct are treated as percentages.
    /// </summary>
    public static decimal ComputeTpFromNetUsd(
        decimal entryPrice,
        decimal executedQty,
        decimal netProfitTargetUsd,
        BotSettingsEntity settings)
    {
        if (entryPrice <= 0 || executedQty <= 0)
            return 0m;

        var entryCost = entryPrice * executedQty;
        var entryFee = entryCost * settings.TakerFeeRate;
        var slippageBuffer = entryCost * (settings.SlippageBufferPct / 100m);
        var spreadBuffer = entryCost * (settings.SpreadBufferPct / 100m);

        // Add a very small extra edge so the realized sell covers rounding / Binance fee drift.
        var microEdge = entryCost * 0.0002m; // 0.02%

        // Net proceeds after exit fee must cover original cost + paid entry fee + desired net + buffers.
        var requiredNetAfterExitFee = entryCost + entryFee + netProfitTargetUsd + slippageBuffer + spreadBuffer + microEdge;
        var effectiveExitFee = Math.Max(settings.MakerFeeRate, 0m);
        var exitValue = requiredNetAfterExitFee / (1m - effectiveExitFee);

        return exitValue / executedQty;
    }

    /// <summary>
    /// Computes fee-aware TP price for a desired gross gain percentage while still padding for Binance fees.
    /// Example: 1.0 means +1.0% target before exchange quantization.
    /// </summary>
    public static decimal ComputeTpFromGrossPercent(
        decimal entryPrice,
        decimal grossTargetPct,
        BotSettingsEntity settings)
    {
        if (entryPrice <= 0)
            return 0m;

        var pct = Math.Max(grossTargetPct, 0m) / 100m;
        var feePct = Math.Max(settings.MakerFeeRate + settings.TakerFeeRate, 0m);
        var bufferPct = Math.Max(settings.SlippageBufferPct + settings.SpreadBufferPct, 0m) / 100m;
        var microEdgePct = 0.0002m; // 0.02%

        return entryPrice * (1m + pct + feePct + bufferPct + microEdgePct);
    }

    /// <summary>
    /// Computes stop loss price from percentage
    /// </summary>
    public static decimal ComputeStopPrice(
        decimal entryPrice,
        decimal stopLossPct)
    {
        return entryPrice * (1m - stopLossPct / 100m);
    }

    /// <summary>
    /// Computes stop limit price (below stop price)
    /// </summary>
    public static decimal ComputeStopLimitPrice(
        decimal stopPrice,
        decimal bufferPct)
    {
        return stopPrice * (1m - bufferPct / 100m);
    }

    /// <summary>
    /// Validates OCO price relationships
    /// </summary>
    public static (bool Valid, string Reason) ValidateOcoPrices(
        decimal entryPrice,
        decimal tpPrice,
        decimal stopPrice,
        decimal stopLimitPrice)
    {
        if (tpPrice <= entryPrice)
            return (false, $"TP {tpPrice} <= Entry {entryPrice}");

        if (stopPrice >= entryPrice)
            return (false, $"Stop {stopPrice} >= Entry {entryPrice}");

        if (stopLimitPrice >= stopPrice)
            return (false, $"StopLimit {stopLimitPrice} >= Stop {stopPrice}");

        return (true, "");
    }
}