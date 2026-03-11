using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Core;

/// <summary>
/// Binance implementation of ITradeExecutor - NO DUPLICATES
/// </summary>
public sealed class BinanceTradeExecutor : ITradeExecutor
{
    private readonly IBinanceRestClient _client;
    private readonly ITradingSafetyGate _safetyGate;
    private readonly ILogger<BinanceTradeExecutor> _logger;

    private static readonly object _rulesLock = new();
    private static readonly SemaphoreSlim _rulesRefreshGate = new(1, 1);
    private static DateTime _rulesLoadedAtUtc = DateTime.MinValue;
    private static Dictionary<string, SymbolTradingRules> _rulesCache = new(StringComparer.OrdinalIgnoreCase);

    public BinanceTradeExecutor(
        IBinanceRestClient client,
        ITradingSafetyGate safetyGate,
        ILogger<BinanceTradeExecutor> logger)
    {
        _client = client;
        _safetyGate = safetyGate;
        _logger = logger;
    }

    // ========================================
    // RULES RETRIEVAL - SINGLE IMPLEMENTATION
    // ========================================
    
    public async Task<SymbolTradingRules?> GetRulesAsync(string symbol, CancellationToken ct)
    {
        try
        {
            // Check cache
            lock (_rulesLock)
            {
                if (_rulesCache.Count > 0 && (DateTime.UtcNow - _rulesLoadedAtUtc) < TimeSpan.FromHours(6))
                    return _rulesCache.TryGetValue(symbol, out var r) ? r : null;
            }

            await _rulesRefreshGate.WaitAsync(ct);
            try
            {
                lock (_rulesLock)
                {
                    if (_rulesCache.Count > 0 && (DateTime.UtcNow - _rulesLoadedAtUtc) < TimeSpan.FromHours(6))
                        return _rulesCache.TryGetValue(symbol, out var cachedAgain) ? cachedAgain : null;
                }

                // Fetch exchange info once for all concurrent callers
                var exInfo = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync(ct: ct);
                if (!exInfo.Success || exInfo.Data is null)
                {
                    lock (_rulesLock)
                    {
                        if (_rulesCache.Count > 0)
                            return _rulesCache.TryGetValue(symbol, out var r) ? r : null;
                    }
                    return null;
                }

                var dict = new Dictionary<string, SymbolTradingRules>(StringComparer.OrdinalIgnoreCase);

            foreach (var s in exInfo.Data.Symbols)
            {
                if (s is null) continue;

                var name = GetStringProp(s, "Name") ?? GetStringProp(s, "Symbol");
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!name.EndsWith("USDT", StringComparison.OrdinalIgnoreCase)) continue;
                if (name.Length < 6 || name.Length > 20) continue;
                if (!name.All(char.IsAsciiLetterOrDigit)) continue;

                var lotObj = GetObjProp(s, "LotSizeFilter");
                var priceObj = GetObjProp(s, "PriceFilter");
                var notionalObj = GetObjProp(s, "MinNotionalFilter");

                var filtersObj = GetObjProp(s, "Filters") as System.Collections.IEnumerable;

                lotObj ??= FindFilter(filtersObj, new[] { "StepSize", "MinQuantity" });
                priceObj ??= FindFilter(filtersObj, new[] { "TickSize" });
                notionalObj ??= FindFilter(filtersObj, new[] { "MinNotional" });

                var step = GetDecimalProp(lotObj, "StepSize");
                var minQty = GetDecimalProp(lotObj, "MinQuantity");
                var tick = GetDecimalProp(priceObj, "TickSize");
                var minNotional = GetDecimalProp(notionalObj, "MinNotional");

                if (step <= 0 || tick <= 0) continue;
                if (minQty <= 0) minQty = step;
                if (minNotional <= 0) minNotional = 0m;

                dict[name] = new SymbolTradingRules(step, minQty, tick, minNotional);
            }

                lock (_rulesLock)
                {
                    _rulesCache = dict;
                    _rulesLoadedAtUtc = DateTime.UtcNow;
                }

                return dict.TryGetValue(symbol, out var rules) ? rules : null;
            }
            finally
            {
                _rulesRefreshGate.Release();
            }
        }
        catch
        {
            lock (_rulesLock)
            {
                if (_rulesCache.Count > 0)
                    return _rulesCache.TryGetValue(symbol, out var r) ? r : null;
            }
            return null;
        }
    }

    // ========================================
    // BUY OPERATIONS - SINGLE IMPLEMENTATIONS
    // ========================================
    
    public async Task<LiveBuyResult> MarketBuyAsync(string symbol, decimal usdtAmount, CancellationToken ct)
    {
        // ✅ SAFETY GATE ENFORCEMENT
        var safety = await _safetyGate.CanPlaceOrderAsync(symbol, "MARKET_BUY", ct);
        
        if (!safety.Allowed)
        {
            return new LiveBuyResult(false, null, 0, 0, 0, safety.Reason);
        }

        if (safety.DryRun)
        {
            return new LiveBuyResult(true, null, 0, 0, 0, "[DRY_RUN] Simulated market buy");
        }

        // Normalize quote quantity to Binance-safe precision (prevents quoteOrderQty precision errors)
        var normalizedQuote = QuantizationService.RoundQuoteToPrecision(usdtAmount, 2);
        if (normalizedQuote <= 0)
            return new LiveBuyResult(false, null, 0, 0, 0, "Invalid quote quantity after precision normalization");

        var buyRes = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: OrderSide.Buy,
            type: SpotOrderType.Market,
            quoteQuantity: normalizedQuote,
            ct: ct);

        if (!buyRes.Success)
            return new LiveBuyResult(false, null, 0, 0, 0, buyRes.Error?.Message);

        var orderId = buyRes.Data.Id;
        var ord = await _client.SpotApi.Trading.GetOrderAsync(symbol, orderId, ct: ct);
        
        if (!ord.Success || ord.Data is null)
            return new LiveBuyResult(true, orderId, 0, 0, 0, null);

        var qty = ord.Data.QuantityFilled;
        var quote = ord.Data.QuoteQuantityFilled;
        var avg = qty > 0 ? quote / qty : 0;

        var rules = await GetRulesAsync(symbol, ct);
        if (rules is not null)
            qty = FloorToStep(qty, rules.StepSize);

        return new LiveBuyResult(true, orderId, qty, avg, quote, null);
    }

    public async Task<LiveBuyResult> LimitBuyMakerAsync(
        string symbol,
        decimal usdtAmount,
        decimal limitPrice,
        CancellationToken ct,
        int timeoutSeconds = 5)
    {
        // ✅ SAFETY GATE ENFORCEMENT
        var safety = await _safetyGate.CanPlaceOrderAsync(symbol, "LIMIT_MAKER_BUY", ct);
        
        if (!safety.Allowed)
        {
            return new LiveBuyResult(false, null, 0, 0, 0, safety.Reason);
        }

        if (safety.DryRun)
        {
            return new LiveBuyResult(true, null, 0, 0, 0, "[DRY_RUN] Simulated limit maker buy");
        }

        // ✅ PARTIAL FILL HANDLING
        var rules = await GetRulesAsync(symbol, ct);
        var tick = rules?.TickSize ?? 0.00000001m;
        var normalizedQuote = QuantizationService.RoundQuoteToPrecision(usdtAmount, 2);
        var price = RoundToTick(limitPrice, tick);
        if (price <= 0)
            return new LiveBuyResult(false, null, 0, 0, 0, "Invalid limit maker price");
        var qty = normalizedQuote / price;

        if (rules is not null)
            qty = FloorToStep(qty, rules.StepSize);

        if (qty <= 0)
            return new LiveBuyResult(false, null, 0, 0, 0, "Invalid quantity");

        var res = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: OrderSide.Buy,
            type: SpotOrderType.LimitMaker,
            quantity: qty,
            price: price,
            ct: ct);

        if (!res.Success)
            return new LiveBuyResult(false, null, 0, 0, 0, res.Error?.Message);

        var orderId = res.Data.Id;
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(timeoutSeconds);
        var requestedQty = qty;

        while (DateTime.UtcNow < until)
        {
            ct.ThrowIfCancellationRequested();

            var ord = await _client.SpotApi.Trading.GetOrderAsync(symbol, orderId, ct: ct);
            if (ord.Success && ord.Data is not null)
            {
                var status = ord.Data.Status;
                var filledQty = ord.Data.QuantityFilled;

                // Fully filled
                if (status == OrderStatus.Filled && filledQty > 0)
                {
                    var fQuote = ord.Data.QuoteQuantityFilled;
                    var avg = filledQty > 0 ? fQuote / filledQty : 0;
                    return new LiveBuyResult(true, orderId, filledQty, avg, fQuote, null);
                }

                // Partially filled - timeout reached
                if (filledQty > 0 && filledQty < requestedQty && DateTime.UtcNow >= until.AddSeconds(-0.5))
                {
                    // ✅ CRITICAL: Cancel remaining order
                    await _client.SpotApi.Trading.CancelOrderAsync(symbol, orderId, ct: ct);
                    await Task.Delay(500, ct); // Wait for cancel confirmation

                    // Accept partial fill if meets minimum requirements
                    if (rules != null)
                    {
                        var normalizedPartial = FloorToStep(filledQty, rules.StepSize);
                        if (normalizedPartial >= rules.MinQty && 
                            normalizedPartial * price >= rules.MinNotional)
                        {
                            var fQuote = ord.Data.QuoteQuantityFilled;
                            var avg = filledQty > 0 ? fQuote / filledQty : 0;
                            
                            return new LiveBuyResult(
                                true,
                                orderId,
                                normalizedPartial,
                                avg,
                                fQuote,
                                $"[PARTIAL_FILL] {normalizedPartial}/{requestedQty}");
                        }
                    }
                }

                // Order canceled/rejected/expired
                if (status == OrderStatus.Canceled ||
                    status == OrderStatus.Expired ||
                    status == OrderStatus.Rejected)
                    break;
            }

            await Task.Delay(250, ct);
        }

        // Timeout - cancel order
        await _client.SpotApi.Trading.CancelOrderAsync(symbol, orderId, ct: ct);
        return new LiveBuyResult(false, orderId, 0, 0, 0, "Timeout - order canceled");
    }

    // ========================================
    // SELL OPERATIONS - SINGLE IMPLEMENTATIONS
    // ========================================
    
    public async Task<LiveSellResult> MarketSellAsync(string symbol, decimal quantity, CancellationToken ct)
    {
        // ✅ SAFETY GATE ENFORCEMENT
        var safety = await _safetyGate.CanPlaceOrderAsync(symbol, "MARKET_SELL", ct);
        
        if (!safety.Allowed)
        {
            return new LiveSellResult(false, null, 0, 0, 0, safety.Reason);
        }

        if (safety.DryRun)
        {
            return new LiveSellResult(true, null, 0, 0, 0, "[DRY_RUN] Simulated market sell");
        }

        // ✅ DOUBLE-SELL PROTECTION: Cancel open orders first
        await CancelAllOpenOrdersAsync(symbol, ct);
        
        // ✅ CRITICAL: Confirm cancellation succeeded
        for (int i = 0; i < 5; i++)
        {
            await Task.Delay(500, ct);
            var hasOrders = await HasAnyOpenOrdersAsync(symbol, ct);
            if (!hasOrders) break;
            
            if (i == 4)
            {
                return new LiveSellResult(false, null, 0, 0, 0, 
                    "EXIT_BLOCKED: Orders still open after 2.5s");
            }
        }

        var rules = await GetRulesAsync(symbol, ct);
        if (rules is not null)
        {
            quantity = FloorToStep(quantity, rules.StepSize);
            if (quantity < rules.MinQty)
                return new LiveSellResult(false, null, 0, 0, 0, 
                    $"Qty {quantity:0.########} below MinQty {rules.MinQty:0.########}");
        }

        var sellRes = await _client.SpotApi.Trading.PlaceOrderAsync(
            symbol: symbol,
            side: OrderSide.Sell,
            type: SpotOrderType.Market,
            quantity: quantity,
            ct: ct);

        if (!sellRes.Success)
            return new LiveSellResult(false, null, 0, 0, 0, sellRes.Error?.Message);

        var orderId = sellRes.Data.Id;
        var ord = await _client.SpotApi.Trading.GetOrderAsync(symbol, orderId, ct: ct);
        
        if (!ord.Success || ord.Data is null)
            return new LiveSellResult(true, orderId, 0, 0, 0, null);

        var qty = ord.Data.QuantityFilled;
        var quote = ord.Data.QuoteQuantityFilled;
        var avg = qty > 0 ? quote / qty : 0;

        return new LiveSellResult(true, orderId, qty, avg, quote, null);
    }

    public async Task<LiveOcoResult> PlaceOcoSellAsync(
        string symbol,
        decimal quantity,
        decimal takeProfitPrice,
        decimal stopPrice,
        decimal stopLimitPrice,
        CancellationToken ct)
    {
        // ✅ SAFETY GATE ENFORCEMENT
        var safety = await _safetyGate.CanPlaceOrderAsync(symbol, "OCO_SELL", ct);
        
        if (!safety.Allowed)
        {
            return new LiveOcoResult(false, null, safety.Reason);
        }

        if (safety.DryRun)
        {
            return new LiveOcoResult(true, null, "[DRY_RUN] Simulated OCO");
        }

        // Original implementation continues...
        var rules = await GetRulesAsync(symbol, ct);

        if (rules is not null)
        {
            // Always sell slightly less than the filled quantity to absorb exchange-side fee/settlement dust.
            var balance = await GetBaseAssetBalanceAsync(symbol, ct);
            var maxSellable = balance.Free > 0 ? balance.Free * 0.998m : quantity * 0.998m;
            quantity = Math.Min(quantity, maxSellable);
            quantity = QuantizationService.NormalizeQtyForExchange(quantity, rules.StepSize);

            takeProfitPrice = QuantizationService.NormalizePriceForExchange(takeProfitPrice, rules.TickSize);
            stopPrice = QuantizationService.NormalizePriceForExchange(stopPrice, rules.TickSize);
            stopLimitPrice = QuantizationService.NormalizePriceForExchange(stopLimitPrice, rules.TickSize);

            // Binance requires stopLimit < stop for sell OCO. Enforce strict relationship after normalization.
            var minStopGap = rules.TickSize * 3;
            if (stopLimitPrice >= stopPrice)
                stopLimitPrice = QuantizationService.NormalizePriceForExchange(stopPrice - minStopGap, rules.TickSize);
            while (stopLimitPrice >= stopPrice && stopLimitPrice > 0)
                stopLimitPrice = QuantizationService.NormalizePriceForExchange(stopLimitPrice - rules.TickSize, rules.TickSize);

            if (takeProfitPrice <= 0 || stopPrice <= 0 || stopLimitPrice <= 0)
                return new LiveOcoResult(false, null, "Invalid OCO prices after normalization");

            if (quantity < rules.MinQty)
                return new LiveOcoResult(false, null, $"Quantity below MinQty after balance clamp: {quantity}");

            if (rules.MinNotional > 0 && (quantity * takeProfitPrice) < rules.MinNotional)
                return new LiveOcoResult(false, null, "Notional below MinNotional");
        }

        _logger.LogInformation("[OCO_SUBMIT] {Symbol} Qty={Qty} TP={TP} Stop={Stop} StopLimit={StopLimit}",
            symbol, quantity, takeProfitPrice, stopPrice, stopLimitPrice);

        var res = await _client.SpotApi.Trading.PlaceOcoOrderAsync(
            symbol: symbol,
            side: OrderSide.Sell,
            quantity: quantity,
            price: takeProfitPrice,
            stopPrice: stopPrice,
            stopLimitPrice: stopLimitPrice,
            stopLimitTimeInForce: TimeInForce.GoodTillCanceled,
            ct: ct);

        if (!res.Success)
        {
            var err = res.Error?.Message ?? "Unknown OCO error";
            // Retry once with current free balance clamped further if Binance reports insufficient balance.
            if (err.Contains("insufficient balance", StringComparison.OrdinalIgnoreCase) && rules is not null)
            {
                var balanceRetry = await GetBaseAssetBalanceAsync(symbol, ct);
                var retryQty = QuantizationService.NormalizeQtyForExchange(balanceRetry.Free * 0.995m, rules.StepSize);
                if (retryQty >= rules.MinQty && retryQty < quantity)
                {
                    _logger.LogWarning("[OCO_RETRY_BALANCE] {Symbol} oldQty={OldQty} retryQty={RetryQty}", symbol, quantity, retryQty);
                    res = await _client.SpotApi.Trading.PlaceOcoOrderAsync(
                        symbol: symbol,
                        side: OrderSide.Sell,
                        quantity: retryQty,
                        price: takeProfitPrice,
                        stopPrice: stopPrice,
                        stopLimitPrice: stopLimitPrice,
                        stopLimitTimeInForce: TimeInForce.GoodTillCanceled,
                        ct: ct);
                    if (res.Success)
                        return new LiveOcoResult(true, res.Data.Id, null);
                    err = res.Error?.Message ?? err;
                }
            }
            _logger.LogWarning("[OCO_REJECTED] {Symbol} {Error}", symbol, err);
            return new LiveOcoResult(false, null, err);
        }

        return new LiveOcoResult(true, res.Data.Id, null);
    }

    // ========================================
    // BALANCE QUERIES - SINGLE IMPLEMENTATIONS
    // ========================================
    
    public async Task<decimal> GetFreeBaseAssetAsync(string symbol, CancellationToken ct)
    {
        var baseAsset = GuessBaseAsset(symbol);
        if (string.IsNullOrWhiteSpace(baseAsset))
            return 0m;

        var acc = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!acc.Success || acc.Data is null) 
            return 0m;

        var bal = acc.Data.Balances
            .FirstOrDefault(b => b.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase));

        return bal?.Available ?? 0m;
    }

    public async Task<(decimal Free, decimal Locked, decimal Total)> GetBaseAssetBalanceAsync(string symbol, CancellationToken ct)
    {
        var baseAsset = GuessBaseAsset(symbol);
        if (string.IsNullOrWhiteSpace(baseAsset))
            return (0, 0, 0);

        var acc = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
        if (!acc.Success || acc.Data is null)
            return (0, 0, 0);

        var bal = acc.Data.Balances
            .FirstOrDefault(b => b.Asset.Equals(baseAsset, StringComparison.OrdinalIgnoreCase));

        if (bal == null)
            return (0, 0, 0);

        return (bal.Available, bal.Locked, bal.Available + bal.Locked);
    }

    // ========================================
    // ORDER MANAGEMENT - SINGLE IMPLEMENTATIONS
    // ========================================
    
    public async Task<bool> HasAnyOpenOrdersAsync(string symbol, CancellationToken ct)
    {
        var open = await _client.SpotApi.Trading.GetOpenOrdersAsync(symbol, ct: ct);
        return open.Success && open.Data is not null && open.Data.Any();
    }

    public async Task<bool> CancelAllOpenOrdersAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var open = await _client.SpotApi.Trading.GetOpenOrdersAsync(symbol, ct: ct);
            if (!open.Success || open.Data is null || !open.Data.Any())
                return true;

            foreach (var o in open.Data)
            {
                await _client.SpotApi.Trading.CancelOrderAsync(symbol, o.Id, ct: ct);
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    // ========================================
    // PRIVATE HELPERS - SINGLE IMPLEMENTATIONS
    // ========================================
    
    private static string GuessBaseAsset(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return "";
        if (symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            return symbol[..^4];
        return "";
    }

    private static decimal FloorToStep(decimal value, decimal step)
    {
        if (step <= 0) return value;
        return Math.Floor(value / step) * step;
    }

    private static decimal RoundToTick(decimal value, decimal tick)
    {
        if (tick <= 0) return value;
        return Math.Round(value / tick, 0, MidpointRounding.ToZero) * tick;
    }

    private static object? GetObjProp(object obj, string prop)
        => obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj);

    private static string? GetStringProp(object obj, string prop)
        => obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance)?.GetValue(obj) as string;

    private static decimal GetDecimalProp(object? obj, string prop)
    {
        if (obj is null) return 0m;
        var pi = obj.GetType().GetProperty(prop, BindingFlags.Public | BindingFlags.Instance);
        if (pi is null) return 0m;
        var v = pi.GetValue(obj);
        if (v is null) return 0m;

        if (v is decimal d) return d;
        if (v is double db) return (decimal)db;
        if (v is float f) return (decimal)f;

        if (decimal.TryParse(Convert.ToString(v, CultureInfo.InvariantCulture), 
            NumberStyles.Any, CultureInfo.InvariantCulture, out var dd))
            return dd;

        return 0m;
    }

    private static object? FindFilter(System.Collections.IEnumerable? filters, string[] mustHaveProps)
    {
        if (filters is null) return null;

        foreach (var f in filters)
        {
            if (f is null) continue;
            var t = f.GetType();

            bool ok = true;
            foreach (var p in mustHaveProps)
            {
                if (t.GetProperty(p, BindingFlags.Public | BindingFlags.Instance) is null)
                {
                    ok = false;
                    break;
                }
            }

            if (ok) return f;
        }

        return null;
    }

    public async Task<TradeResult> PlaceLimitSellAsync(
        string symbol, 
        decimal quantity, 
        decimal price, 
        CancellationToken ct)
    {
        try
        {
            var order = await _client.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                Binance.Net.Enums.OrderSide.Sell,
                Binance.Net.Enums.SpotOrderType.Limit,
                quantity: quantity,
                price: price,
                timeInForce: Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                ct: ct);

            if (!order.Success)
                return new TradeResult(
                    false,
                    order.Error?.Message ?? "Unknown error",
                    null,
                    null,
                    null
                );

            return new TradeResult(
                true,
                null,
                order.Data.Price != 0m ? order.Data.Price : price,
                order.Data.QuantityFilled,
                order.Data.Id
            );
        }
        catch (Exception ex)
        {
            return new TradeResult(
                false,
                ex.Message,
                null,
                null,
                null
            );
        }
    }

    public async Task<TradeResult> PlaceStopLossLimitAsync(
        string symbol, 
        decimal quantity, 
        decimal stopPrice, 
        decimal limitPrice, 
        CancellationToken ct)
    {
        try
        {
            var order = await _client.SpotApi.Trading.PlaceOrderAsync(
                symbol,
                Binance.Net.Enums.OrderSide.Sell,
                Binance.Net.Enums.SpotOrderType.StopLossLimit,
                quantity: quantity,
                price: limitPrice,
                stopPrice: stopPrice,
                timeInForce: Binance.Net.Enums.TimeInForce.GoodTillCanceled,
                ct: ct);

            if (!order.Success)
                return new TradeResult(
                    false,
                    order.Error?.Message ?? "Unknown error",
                    null,
                    null,
                    null
                );

            return new TradeResult(
                true,
                null,
                stopPrice,
                order.Data.QuantityFilled,
                order.Data.Id
            );
        }
        catch (Exception ex)
        {
            return new TradeResult(
                false,
                ex.Message,
                null,
                null,
                null
            );
        }
    }
}
