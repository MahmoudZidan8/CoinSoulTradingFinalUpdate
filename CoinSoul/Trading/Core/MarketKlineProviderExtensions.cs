using Binance.Net.Enums;
using CoinSoul.Trading.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSoul.Trading.Engine;

public static class MarketKlineProviderExtensions
{
    // Overload if old code calls without ct
    public static Task<IReadOnlyList<decimal>> GetClosesAsync(
        this IMarketKlineProvider provider,
        string symbol,
        KlineInterval interval,
        int count)
        => provider.GetClosesAsync(symbol, interval, count, CancellationToken.None);

    // If your provider doesn't have highs/lows, we derive via reflection from GetKlinesAsync if it exists
    public static async Task<IReadOnlyList<decimal>> GetHighsAsync(
        this IMarketKlineProvider provider,
        string symbol,
        KlineInterval interval,
        int count,
        CancellationToken ct)
    {
        var klines = await TryGetKlinesAsync(provider, symbol, interval, count, ct);
        if (klines != null) return klines.Select(ReadDecimalProp("HighPrice") ?? ReadDecimalProp("High")).ToList();
        // fallback
        var closes = await provider.GetClosesAsync(symbol, interval, count, ct);
        return closes.ToList();
    }

    public static async Task<IReadOnlyList<decimal>> GetLowsAsync(
        this IMarketKlineProvider provider,
        string symbol,
        KlineInterval interval,
        int count,
        CancellationToken ct)
    {
        var klines = await TryGetKlinesAsync(provider, symbol, interval, count, ct);
        if (klines != null) return klines.Select(ReadDecimalProp("LowPrice") ?? ReadDecimalProp("Low")).ToList();
        // fallback
        var closes = await provider.GetClosesAsync(symbol, interval, count, ct);
        return closes.ToList();
    }

    private static Func<object, decimal>? ReadDecimalProp(string propName)
        => (obj) =>
        {
            var p = obj.GetType().GetProperty(propName, BindingFlags.Instance | BindingFlags.Public);
            if (p == null) return 0m;
            var v = p.GetValue(obj);
            if (v == null) return 0m;
            if (v is decimal d) return d;
            if (v is double db) return (decimal)db;
            if (v is float f) return (decimal)f;
            if (decimal.TryParse(v.ToString(), out var x)) return x;
            return 0m;
        };

    private static async Task<IReadOnlyList<object>?> TryGetKlinesAsync(
        IMarketKlineProvider provider,
        string symbol,
        KlineInterval interval,
        int count,
        CancellationToken ct)
    {
        var t = provider.GetType();
        var m = t.GetMethod("GetKlinesAsync", BindingFlags.Instance | BindingFlags.Public);
        if (m == null) return null;

        var ps = m.GetParameters();

        object? result = null;

        // common signatures:
        // GetKlinesAsync(string, KlineInterval, int, CancellationToken)
        if (ps.Length == 4)
        {
            result = m.Invoke(provider, new object?[] { symbol, interval, count, ct });
        }
        // GetKlinesAsync(string, KlineInterval, int)
        else if (ps.Length == 3)
        {
            result = m.Invoke(provider, new object?[] { symbol, interval, count });
        }
        else
        {
            return null;
        }

        if (result is Task task)
        {
            await task.ConfigureAwait(false);
            var prop = task.GetType().GetProperty("Result");
            var res = prop?.GetValue(task);
            if (res is System.Collections.IEnumerable en)
            {
                var list = new List<object>();
                foreach (var x in en) if (x != null) list.Add(x);
                return list;
            }
        }

        return null;
    }
}
