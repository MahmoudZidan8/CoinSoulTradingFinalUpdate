using System;
using System.Collections.Generic;
using System.Linq;

namespace CoinSoul.Trading.Engine;

public static class Indicators
{
    // Convenience overloads (avoid List<decimal> -> decimal[] compile errors)
    public static decimal Sma(IReadOnlyList<decimal> closes, int period) => Sma(closes.ToArray(), period);
    public static decimal Ema(IReadOnlyList<decimal> closes, int period) => Ema(closes.ToArray(), period);
    public static decimal Rsi(IReadOnlyList<decimal> closes, int period = 14) => Rsi(closes.ToArray(), period);
    public static decimal MomentumPct(IReadOnlyList<decimal> closes, int lookback) => MomentumPct(closes.ToArray(), lookback);

    public static decimal Sma(decimal[] closes, int period)
    {
        if (closes.Length == 0) return 0m;
        if (period <= 1) return closes[closes.Length - 1];

        var n = Math.Min(period, closes.Length);
        decimal sum = 0;
        for (int i = closes.Length - n; i < closes.Length; i++)
            sum += closes[i];

        return sum / n;
    }

    public static decimal Ema(decimal[] values, int period)
    {
        if (values.Length == 0) return 0m;
        if (period <= 1) return values[values.Length - 1];

        var alpha = 2m / (period + 1m);
        decimal ema = values[0];
        for (int i = 1; i < values.Length; i++)
            ema = alpha * values[i] + (1 - alpha) * ema;

        return ema;
    }

    public static decimal Rsi(decimal[] closes, int period = 14)
    {
        if (closes.Length < period + 1) return 50m;

        decimal gain = 0m, loss = 0m;
        for (int i = closes.Length - period; i < closes.Length; i++)
        {
            var diff = closes[i] - closes[i - 1];
            if (diff >= 0) gain += diff;
            else loss -= diff;
        }

        if (loss == 0) return 100m;
        var rs = gain / loss;
        return 100m - (100m / (1m + rs));
    }

    public static decimal MomentumPct(decimal[] closes, int lookback)
    {
        if (closes.Length < lookback + 1) return 0m;

        var last = closes[closes.Length - 1];
        var prev = closes[closes.Length - 1 - lookback];
        if (prev <= 0) return 0m;

        return (last - prev) / prev * 100m;
    }

    // ✅ simple change % (for logging/scoring)
   
    public static decimal ChangePct(decimal[] closes, int lookback)
    {
        if (closes is null || closes.Length < lookback + 1) return 0m;
        var last = closes[closes.Length - 1];
        var prev = closes[closes.Length - 1 - lookback];
        if (prev == 0) return 0m;
        return ((last - prev) / prev) * 100m;
    }

    // ✅ ATR% approximation using closes فقط (بدون highs/lows)
    public static decimal AtrPctFromCloses(decimal[] closes, int period = 14)
    {
        if (closes.Length < period + 1) return 0m;

        var n = Math.Min(period, closes.Length - 1);
        decimal sum = 0m;
        for (int i = closes.Length - n; i < closes.Length; i++)
        {
            var tr = Math.Abs(closes[i] - closes[i - 1]);
            sum += tr;
        }

        var atr = sum / n;
        var last = closes[^1];
        if (last <= 0) return 0m;
        return atr / last * 100m;
    }

    // ✅ volatility% (stddev of returns)
    public static decimal VolatilityPct(decimal[] closes, int period = 20)
    {
        if (closes.Length < period + 1) return 0m;

        var n = Math.Min(period, closes.Length - 1);
        var rets = new List<decimal>(n);

        for (int i = closes.Length - n; i < closes.Length; i++)
        {
            var prev = closes[i - 1];
            if (prev <= 0) continue;
            rets.Add((closes[i] - prev) / prev);
        }

        if (rets.Count < 2) return 0m;

        var mean = rets.Average();
        decimal var = 0m;
        foreach (var r in rets)
        {
            var d = r - mean;
            var += d * d;
        }

        var /= (rets.Count - 1);
        var std = (decimal)Math.Sqrt((double)var);
        return std * 100m;
    }
}
