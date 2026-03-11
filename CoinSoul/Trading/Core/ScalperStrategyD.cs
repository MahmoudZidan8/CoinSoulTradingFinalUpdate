using CoinSoul.BinanceService.AutoServices.AccountDataService;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Engine;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Core;

public sealed class ScalperStrategyD : ITradingStrategy
{
    private readonly CoinSoulDbContext _db;
    private readonly IAutoAccountDataService _account;
    private readonly SymbolQueueManager _queue;
    private readonly ITradeExecutor _exec;

    public ScalperStrategyD(
        CoinSoulDbContext db,
        IAutoAccountDataService account,
        SymbolQueueManager queue,
        ITradeExecutor exec)
    {
        _db = db;
        _account = account;
        _queue = queue;
        _exec = exec;
    }

    public async Task EvaluateAsync(
        BotMarketSnapshot market,
        BotState state,
        CancellationToken ct)
    {
        // 1) Load settings (ENTITY) + convert to DOMAIN
        var settingsEntity = await _db.BotSettings.AsNoTracking().FirstAsync(ct);
        var settings = settingsEntity.ToDomain();

        // 2) keep queue refreshed always
        state.AddLog("INFO", $"Queue snapshot: {string.Join(", ", _queue.Snapshot().Select(q => q.Symbol))}");

        // 3) Manage open position (time-exit / sl fallback etc.)
        var pos = await _db.Positions
            .OrderByDescending(p => p.OpenedAtUtc)
            .FirstOrDefaultAsync(p => p.IsOpen, ct);

        if (pos is not null)
        {
            await ManageOpenPositionAsync(pos, settingsEntity, state, ct);
            return;
        }

        // 4) Get next symbol from QUEUE
        var q = await _queue.DequeueAsync(
            settings,
            msg => state.AddLog("INFO", msg),
            ct);

        if (q is null)
        {
            await AddEvent("INFO", "QueueEmpty", "No symbol available in queue", null, null, ct);
            return;
        }

        var symbol = q.Symbol;

        // 5) Balance sizing
        var usdtFree = await _account.GetFreeUsdtAsync(ct);
        if (usdtFree <= 1m)
        {
            await AddEvent("WARN", "NoBalance", $"USDT free too low: {usdtFree:0.00}", null, symbol, ct);
            _queue.Requeue(symbol);
            return;
        }

        var tradeUsdt = Math.Min(usdtFree, settings.MaxUsdPerTrade);

        if (tradeUsdt < 3m)
        {
            await AddEvent("WARN", "NoBalance", $"TradeUSDT too low: {tradeUsdt:0.00}", null, symbol, ct);
            _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(3));
            return;
        }

        // 5) BUY (Professional: LimitMaker 3s then Market fallback)
        state.AddLog("INFO", $"[ENTRY] TRY LIMIT-MAKER {symbol} usdt={tradeUsdt:0.00} (why={q.Reason})");

        var buy = await _exec.LimitBuyMakerAsync(symbol, tradeUsdt, 3m, ct);

        if (!buy.Success)
        {
            state.AddLog("WARN", $"[LIMIT_MAKER_FAIL] {symbol}: {buy.Error} -> fallback MARKET");
            buy = await _exec.MarketBuyAsync(symbol, tradeUsdt, ct);
        }

        if (!buy.Success || buy.ExecutedQty <= 0 || buy.AvgPrice <= 0)
        {
            await AddEvent("ERROR", "BuyFailed", buy.Error ?? "Unknown buy error", null, symbol, ct);
            _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(3));
            return;
        }

        // بعد الشراء: OCO فوراً (المشكلة كانت rules)


        // Save position
        var newPos = new PositionEntity
        {
            Symbol = symbol,
            IsOpen = true,
            EntryPrice = buy.AvgPrice,
            Quantity = buy.ExecutedQty,
            QuoteUsedUsdt = tradeUsdt,
            OpenedAtUtc = DateTime.UtcNow,
            BuyOrderId = buy.OrderId
        };

        _db.Positions.Add(newPos);
        await _db.SaveChangesAsync(ct);

        // Place OCO
        var okOco = await PlaceOcoTpAsync(newPos, settingsEntity, ct);
        if (!okOco)
        {
            state.AddLog("ERROR", $"[OCO_FAIL_FATAL] {symbol} -> fallback MarketSell safety");
            var sell = await _exec.MarketSellAsync(symbol, newPos.Quantity, ct);

            await AddEvent(
                sell.Success ? "TRADE" : "ERROR",
                sell.Success ? "EXIT" : "EXIT_FAIL",
                sell.Success
                    ? $"{symbol} exit=SafetyExit price={sell.AvgPrice:0.########}"
                    : $"{symbol}: {sell.Error}",
                newPos.Id,
                symbol,
                ct);

            newPos.IsOpen = false;
            newPos.ClosedAtUtc = DateTime.UtcNow;
            newPos.ExitReasonValue = ExitReason.SafetyExit.ToString();
            newPos.ExitPrice = sell.Success ? sell.AvgPrice : null;
            newPos.NetPnlUsdt = sell.Success ? (sell.AvgPrice - newPos.EntryPrice) * sell.ExecutedQty : 0m;

            await _db.SaveChangesAsync(ct);

            _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(10));
            return;
        }

        _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(3));

    }

    // =========================
    // Open Position Management
    // =========================
    private async Task ManageOpenPositionAsync(PositionEntity pos, BotSettingsEntity settings, BotState state, CancellationToken ct)
    {
        var age = DateTime.UtcNow - pos.OpenedAtUtc;

        // لو مفيش OCO متسجل (فشل قبل كده) حاول مرة تانية بسرعة (Self-Heal)
        if (pos.OcoOrderId is null && age.TotalSeconds >= 10)
        {
            state.AddLog("WARN", $"[OCO_MISSING] {pos.Symbol} trying to place OCO again...");
            await TryPlaceOcoOrFailSafeSellAsync(pos, settings, state, ct);
        }

        // Time exit
        if (age.TotalMinutes >= Math.Max(1, settings.TimeExitMinutes))
        {
            state.AddLog("WARN", $"[TIME_EXIT] {pos.Symbol} age={age.TotalMinutes:0.0}m -> MarketSell");
            await FailSafeMarketExitAsync(pos, "TimeExit", state, ct);
        }
    }

    // =========================
    // OCO + FailSafe
    // =========================
    private async Task<bool> TryPlaceOcoOrFailSafeSellAsync(PositionEntity pos, BotSettingsEntity settings, BotState state, CancellationToken ct)
    {
        // retry rules 2 مرات (لأن RULES_FAIL غالبًا Network/DNS)
        SymbolTradingRules? rules = null;
        for (var i = 0; i < 2; i++)
        {
            rules = await _exec.GetRulesAsync(pos.Symbol, ct);
            if (rules is not null) break;
            await Task.Delay(250, ct);
        }

        if (rules is null)
        {
            state.AddLog("ERROR", $"[RULES_FAIL] {pos.Symbol} -> FailSafe MarketSell");
            await AddEvent("ERROR", "RULES_FAIL", $"{pos.Symbol}: rules not found", pos.Id, pos.Symbol, ct);

            await FailSafeMarketExitAsync(pos, "RulesFailFallback", state, ct);
            return false;
        }

        // حساب TP/SL
        var feeRate = 0.001m; // تقريب محافظ
        var required =
            settings.NetProfitTargetUsd +
            (pos.QuoteUsedUsdt * feeRate) +
            (pos.EntryPrice * pos.Quantity * feeRate) +
            settings.SlippageBufferUsd;

        var tp = pos.EntryPrice + (required / pos.Quantity);
        var sl = pos.EntryPrice * (1m - (settings.HardStopLossPct / 100m));
        var slLimit = sl * 0.999m;

        // Normalize to tick
        tp = FloorToTick(tp, rules.TickSize);
        sl = FloorToTick(sl, rules.TickSize);
        slLimit = FloorToTick(slLimit, rules.TickSize);

        state.AddLog("INFO", $"[OCO] TRY {pos.Symbol} tp={tp:0.########} stop={sl:0.########} stopLimit={slLimit:0.########}");

        var oco = await _exec.PlaceOcoSellAsync(
            pos.Symbol,
            pos.Quantity,
            tp,
            sl,
            slLimit,
            ct);

        if (!oco.Success || oco.OrderListId is null)
        {
            state.AddLog("ERROR", $"[OCO_FAIL] {pos.Symbol}: {oco.Error} -> FailSafe MarketSell");
            await AddEvent("ERROR", "OCO_FAIL", $"{pos.Symbol}: {oco.Error}", pos.Id, pos.Symbol, ct);

            await FailSafeMarketExitAsync(pos, "OcoFailFallback", state, ct);
            return false;
        }

        // Success
        pos.OcoOrderId = oco.OrderListId;
        await _db.SaveChangesAsync(ct);

        await AddEvent("INFO", "OCO_OK", $"{pos.Symbol} OCO listId={oco.OrderListId}", pos.Id, pos.Symbol, ct);
        state.AddLog("INFO", $"[OCO_OK] {pos.Symbol} listId={oco.OrderListId}");

        return true;
    }
    private async Task<bool> PlaceOcoTpAsync(PositionEntity pos, BotSettingsEntity settings, CancellationToken ct)
    {
        var rules = await _exec.GetRulesAsync(pos.Symbol, ct);
        if (rules is null)
        {
            await AddEvent("ERROR", "RULES_FAIL", $"{pos.Symbol}: rules not found", pos.Id, pos.Symbol, ct);
            return false;
        }

        var feeRate = 0.001m;
        var required =
            settings.NetProfitTargetUsd +
            (pos.QuoteUsedUsdt * feeRate) +
            (pos.EntryPrice * pos.Quantity * feeRate) +
            settings.SlippageBufferUsd;

        var tp = pos.EntryPrice + (required / pos.Quantity);
        var sl = pos.EntryPrice * (1m - settings.HardStopLossPct / 100m);
        var slLimit = sl * 0.999m;

        // tick rounding
        tp = Math.Floor(tp / rules.TickSize) * rules.TickSize;
        sl = Math.Floor(sl / rules.TickSize) * rules.TickSize;
        slLimit = Math.Floor(slLimit / rules.TickSize) * rules.TickSize;

        var oco = await _exec.PlaceOcoSellAsync(pos.Symbol, pos.Quantity, tp, sl, slLimit, ct);
        if (!oco.Success)
        {
            await AddEvent("ERROR", "OCO_FAIL", $"{pos.Symbol}: {oco.Error}", pos.Id, pos.Symbol, ct);
            return false;
        }

        pos.OcoOrderId = oco.OrderListId;
        await _db.SaveChangesAsync(ct);

        await AddEvent("INFO", "OCO_OK", $"{pos.Symbol}: listId={oco.OrderListId}", pos.Id, pos.Symbol, ct);
        return true;
    }

    private async Task FailSafeMarketExitAsync(PositionEntity pos, string reason, BotState state, CancellationToken ct)
    {
        // لو المستخدم باع يدويًا: هيفشل insufficient balance — ساعتها نقفل الصفقة في DB كـ ExternalClose
        var sell = await _exec.MarketSellAsync(pos.Symbol, pos.Quantity, ct);

        if (!sell.Success)
        {
            await AddEvent("ERROR", "EXIT_FAIL", $"{pos.Symbol}: {sell.Error}", pos.Id, pos.Symbol, ct);

            // ✅ External close heuristic
            // لو الخطأ Insufficient balance غالبًا الصفقة اتقفلت يدويًا أو OCO اتنفذ
            var err = (sell.Error ?? "").ToLowerInvariant();
            if (err.Contains("insufficient balance") || err.Contains("account has insufficient balance"))
            {
                pos.IsOpen = false;
                pos.ClosedAtUtc = DateTime.UtcNow;
                pos.ExitReasonValue = "ExternalClose";
                await _db.SaveChangesAsync(ct);

                state.AddLog("WARN", $"[EXTERNAL_CLOSE] {pos.Symbol} marked closed in DB (manual/OCO executed).");
                await AddEvent("WARN", "EXTERNAL_CLOSE", $"{pos.Symbol} closed externally (manual/OCO).", pos.Id, pos.Symbol, ct);
            }

            return;
        }

        pos.IsOpen = false;
        pos.ClosedAtUtc = DateTime.UtcNow;
        pos.SellOrderId = sell.OrderId;
        pos.ExitPrice = sell.AvgPrice;
        pos.ExitReasonValue = reason;
        pos.NetPnlUsdt = (sell.AvgPrice - pos.EntryPrice) * sell.ExecutedQty;

        await _db.SaveChangesAsync(ct);

        state.AddLog("TRADE", $"[EXIT] {pos.Symbol} reason={reason} price={sell.AvgPrice:0.########} pnl={pos.NetPnlUsdt:0.00}");
        await AddEvent("TRADE", "EXIT", $"{pos.Symbol} exit={reason} price={sell.AvgPrice:0.########} pnl={pos.NetPnlUsdt:0.00}", pos.Id, pos.Symbol, ct);
    }

    private static decimal FloorToTick(decimal value, decimal tick)
    {
        if (tick <= 0) return value;
        return Math.Floor(value / tick) * tick;
    }

    private async Task AddEvent(
        string level,
        string type,
        string msg,
        int? posId,
        string? symbol,
        CancellationToken ct)
    {
        _db.Events.Add(new EventEntity
        {
            Level = level,
            Type = type,
            Message = msg,
            PositionId = posId,
            Symbol = symbol,
            AtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }
}
