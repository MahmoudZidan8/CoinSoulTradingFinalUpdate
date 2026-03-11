using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public static class TradingRulesNormalizer
{
    public static NormalizeQuantityResult NormalizeQuantity(decimal quantity, SymbolTradingRules? rules)
    {
        if (rules == null)
            return new NormalizeQuantityResult 
            { 
                NormalizedQty = quantity, 
                Valid = false, 
                Ok = false,
                Reason = "Rules unavailable",
                Why = "Rules unavailable"
            };

        var normalized = ProfitTargetCalculator.FloorToStepSize(quantity, rules.StepSize);

        if (normalized < rules.MinQty)
        {
            return new NormalizeQuantityResult 
            { 
                NormalizedQty = normalized, 
                Valid = false,
                Ok = false,
                Reason = $"Qty {normalized:0.########} < MinQty {rules.MinQty:0.########}",
                Why = $"Qty {normalized:0.########} < MinQty {rules.MinQty:0.########}"
            };
        }

        return new NormalizeQuantityResult 
        { 
            NormalizedQty = normalized, 
            Valid = true,
            Ok = true,
            Reason = "OK",
            Why = "OK"
        };
    }

    public static decimal NormalizePrice(decimal price, SymbolTradingRules? rules)
    {
        if (rules == null || rules.TickSize <= 0)
            return price;

        return ProfitTargetCalculator.RoundToTickSize(price, rules.TickSize);
    }

    public static ValidateNotionalResult ValidateNotional(decimal price, decimal quantity, SymbolTradingRules? rules)
    {
        if (rules == null)
            return new ValidateNotionalResult 
            { 
                Valid = false, 
                Ok = false,
                Reason = "Rules unavailable",
                Why = "Rules unavailable"
            };

        var notional = price * quantity;

        if (rules.MinNotional > 0 && notional < rules.MinNotional)
        {
            return new ValidateNotionalResult 
            { 
                Valid = false,
                Ok = false,
                Reason = $"Notional {notional:0.00} < MinNotional {rules.MinNotional:0.00}",
                Why = $"Notional {notional:0.00} < MinNotional {rules.MinNotional:0.00}"
            };
        }

        return new ValidateNotionalResult 
        { 
            Valid = true,
            Ok = true,
            Reason = "OK",
            Why = "OK"
        };
    }

    public static NormalizeOrderResult NormalizeOrder(decimal quantity, decimal price, SymbolTradingRules? rules)
    {
        if (rules == null)
            return new NormalizeOrderResult 
            { 
                NormalizedQty = quantity, 
                NormalizedPrice = price, 
                Valid = false,
                Ok = false,
                Reason = "Rules unavailable",
                Why = "Rules unavailable"
            };

        var qtyResult = NormalizeQuantity(quantity, rules);
        if (!qtyResult.Valid)
            return new NormalizeOrderResult 
            { 
                NormalizedQty = qtyResult.NormalizedQty, 
                NormalizedPrice = price, 
                Valid = false,
                Ok = false,
                Reason = qtyResult.Reason,
                Why = qtyResult.Why
            };

        var normPrice = NormalizePrice(price, rules);
        var notionalResult = ValidateNotional(normPrice, qtyResult.NormalizedQty, rules);
        
        if (!notionalResult.Valid)
            return new NormalizeOrderResult 
            { 
                NormalizedQty = qtyResult.NormalizedQty, 
                NormalizedPrice = normPrice, 
                Valid = false,
                Ok = false,
                Reason = notionalResult.Reason,
                Why = notionalResult.Why
            };

        return new NormalizeOrderResult 
        { 
            NormalizedQty = qtyResult.NormalizedQty, 
            NormalizedPrice = normPrice, 
            Valid = true,
            Ok = true,
            Reason = "OK",
            Why = "OK"
        };
    }
}

public sealed class NormalizeQuantityResult
{
    public decimal NormalizedQty { get; set; }
    public bool Valid { get; set; }
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public string? Why { get; set; }
}

public sealed class ValidateNotionalResult
{
    public bool Valid { get; set; }
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public string? Why { get; set; }
}

public sealed class NormalizeOrderResult
{
    public decimal NormalizedQty { get; set; }
    public decimal NormalizedPrice { get; set; }
    public bool Valid { get; set; }
    public bool Ok { get; set; }
    public string? Reason { get; set; }
    public string? Why { get; set; }
}