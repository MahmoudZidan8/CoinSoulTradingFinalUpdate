using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public static class SymbolRulesNormalizer
{
    public static decimal NormalizePrice(decimal price, decimal tickSize)
    {
        if (tickSize <= 0)
            return price;
        
        return Math.Floor(price / tickSize) * tickSize;
    }

    public static NormalizeQuantityResult NormalizeQty(decimal qty, decimal stepSize, decimal minQty)
    {
        if (stepSize <= 0)
            return new NormalizeQuantityResult { NormalizedQty = qty, Ok = false, Why = "Invalid stepSize" };
        
        var normalized = Math.Floor(qty / stepSize) * stepSize;
        
        if (normalized < minQty)
            return new NormalizeQuantityResult { NormalizedQty = normalized, Ok = false, Why = $"Qty {normalized:0.########} < MinQty {minQty:0.########}" };
        
        return new NormalizeQuantityResult { NormalizedQty = normalized, Ok = true, Why = "OK" };
    }

    public static ValidateNotionalResult EnsureNotional(decimal price, decimal qty, decimal minNotional)
    {
        var notional = price * qty;
        
        if (minNotional > 0 && notional < minNotional)
            return new ValidateNotionalResult { Ok = false, Why = $"Notional {notional:0.00} < MinNotional {minNotional:0.00}" };
        
        return new ValidateNotionalResult { Ok = true, Why = "OK" };
    }
}

//public sealed class NormalizeQuantityResult
//{
//    public decimal Qty { get; set; }
//    public bool Ok { get; set; }
//    public string Why { get; set; } = "";
//}

//public sealed class ValidateNotionalResult
//{
//    public bool Ok { get; set; }
//    public string Why { get; set; } = "";
//}