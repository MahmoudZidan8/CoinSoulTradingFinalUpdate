namespace CoinSoul.Trading.Engine;

public sealed class NetProfitTargetService
{
    /// <summary>
    /// Computes required take profit price to achieve net profit target after all fees and buffers
    /// </summary>
    public decimal ComputeRequiredTakeProfitPrice(
        decimal entryPrice,
        decimal qty,
        decimal minNetProfitUsd,
        decimal makerFeeRate,
        decimal takerFeeRate,
        decimal slippageBufferPct,
        decimal spreadBufferPct,
        bool useMakerForTp = true)
    {
        var entryNotional = entryPrice * qty;
        var entryFeeUsd = entryNotional * takerFeeRate;
        var slippageUsd = entryNotional * slippageBufferPct;
        var spreadUsd = entryNotional * spreadBufferPct;
        
        var exitFeeRate = useMakerForTp ? makerFeeRate : takerFeeRate;
        
        var totalDeductions = entryFeeUsd + slippageUsd + spreadUsd + minNetProfitUsd;
        
        var numerator = (entryPrice * qty) + totalDeductions;
        var denominator = qty * (1m - exitFeeRate);
        
        if (denominator <= 0)
            return entryPrice * 1.02m;
        
        var tpPrice = numerator / denominator;
        
        return tpPrice;
    }

    /// <summary>
    /// Computes stop loss price based on nominal percentage
    /// </summary>
    public decimal ComputeStopPrice(decimal entryPrice, decimal slNominalPct)
    {
        return entryPrice * (1m - (slNominalPct / 100m));
    }

    /// <summary>
    /// Computes stop limit price with buffer below stop price
    /// </summary>
    public decimal ComputeStopLimitPrice(decimal stopPrice, decimal stopLimitBufferPct)
    {
        return stopPrice * (1m - stopLimitBufferPct);
    }
}