using System.Text;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// ✅ HOTFIX: Centralized symbol sanitization and validation (ISSUE #2).
/// Prevents invalid symbols like "????USDT" from entering queue or WebSocket subscriptions.
/// </summary>
public static class SymbolSanitizer
{
    /// <summary>
    /// Clean raw symbol input by removing non-alphanumeric characters.
    /// </summary>
    public static string Clean(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        var s = raw.Trim().ToUpperInvariant();

        // Keep only A-Z and 0-9 (remove hidden chars, arabic/emoji, etc.)
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s)
        {
            if ((ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'))
                sb.Append(ch);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Validate if symbol matches Binance USDT pair format.
    /// Format: {BASE}USDT where BASE is 2-12 alphanumeric chars.
    /// Examples: BTCUSDT, ETHUSDT, BNB2USDT
    /// </summary>
    public static bool IsValidUsdtSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        if (!symbol.EndsWith("USDT", StringComparison.Ordinal)) return false;

        var baseAsset = symbol[..^4];
        if (baseAsset.Length < 2 || baseAsset.Length > 12) return false;

        // base must be alnum only
        return baseAsset.All(ch => (ch >= 'A' && ch <= 'Z') || (ch >= '0' && ch <= '9'));
    }
}