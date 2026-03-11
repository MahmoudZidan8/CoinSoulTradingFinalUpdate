using CoinSoul.Trading.Core;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace CoinSoul.Trading.Engine;

public static class SymbolValidatorExtensions
{
    // call it like: await _validator.IsValidAsync(symbol, ct)
    public static async Task<bool> IsValidAsync(this ISymbolValidator validator, string symbol, CancellationToken ct = default)
    {
        // 1) basic safety (prevents garbage symbols like "币安人生USDT")
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        symbol = symbol.Trim().ToUpperInvariant();

        // must be ASCII letters/numbers, common Binance format
        if (!Regex.IsMatch(symbol, @"^[A-Z0-9]{2,20}USDT$"))
            return false;

        // 2) try to call any existing method on your validator by reflection
        // supports common names: IsValid, IsValidAsync, Validate, ValidateAsync, ValidateSymbolAsync...
        var t = validator.GetType();

        // async methods (Task<bool>)
        foreach (var name in new[] { "IsValidAsync", "ValidateAsync", "ValidateSymbolAsync" })
        {
            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            if (m == null) continue;

            var ps = m.GetParameters();
            try
            {
                object? result;
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(CancellationToken))
                    result = m.Invoke(validator, new object?[] { symbol, ct });
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    result = m.Invoke(validator, new object?[] { symbol });
                else
                    continue;

                if (result is Task<bool> tb) return await tb;
            }
            catch { /* ignore */ }
        }

        // sync methods (bool)
        foreach (var name in new[] { "IsValid", "Validate", "ValidateSymbol" })
        {
            var m = t.GetMethod(name, BindingFlags.Instance | BindingFlags.Public);
            if (m == null) continue;

            var ps = m.GetParameters();
            try
            {
                object? result;
                if (ps.Length == 2 && ps[0].ParameterType == typeof(string) && ps[1].ParameterType == typeof(CancellationToken))
                    result = m.Invoke(validator, new object?[] { symbol, ct });
                else if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    result = m.Invoke(validator, new object?[] { symbol });
                else
                    continue;

                if (result is bool b) return b;
            }
            catch { /* ignore */ }
        }

        // 3) fallback: accept regex only
        return true;
    }
}
