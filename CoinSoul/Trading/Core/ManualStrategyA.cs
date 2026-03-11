using System.Collections.Concurrent;
using Binance.Net.Enums;
using CoinSoul.Trading.Engine;

namespace CoinSoul.Trading.Core;

public sealed class ManualStrategyA : ITradingStrategy
{
    private readonly IMarketKlineProvider _klines;
    private readonly ITradeExecutor _exec;

    private readonly ConcurrentDictionary<string, KlineCache> _cache = new();

    private sealed class KlineCache
    {
        public DateTimeOffset UpdatedAtUtc { get; set; }
        public List<decimal> Closes { get; set; } = new();
    }

    public ManualStrategyA(IMarketKlineProvider klines, ITradeExecutor exec)
    {
        _klines = klines;
        _exec = exec;
    }

    public async Task EvaluateAsync(BotMarketSnapshot market, BotState state, CancellationToken ct)
    {
    //    foreach (var cfg in state.Settings.ManualSymbols.Where(x => x.Enabled))
    //    {
    //        if (!market.Prices.TryGetValue(cfg.Symbol, out var lastPrice))
    //            continue;

    //        if (lastPrice <= 0)
    //            continue;

    //        // Global max open trades
    //        if (!state.CanOpenMoreTrades())
    //            continue;

    //        // Per-symbol multi-entry (use cfg.MaxOpenTrades + Settings.MaxEntriesPerSymbol)
    //        var perSymbolMax = cfg.MaxOpenTrades > 0 ? cfg.MaxOpenTrades : state.Settings.MaxEntriesPerSymbol;
    //        if (!state.CanOpenForSymbol(cfg.Symbol, perSymbolMax))
    //        {
    //            state.AddLog("DEBUG", $"SKIP {cfg.Symbol} - per symbol max reached ({perSymbolMax})");
    //            continue;
    //        }

    //        if (state.IsSymbolInCooldown(cfg.Symbol))
    //            continue;

    //        if (state.DailyLossUsd >= state.Settings.DailyLossLimitUsd)
    //            break;

    //        var mode = state.Settings.StrategyAMode;

    //        // Mode parameters
    //        var (interval, limit, refreshSec, rsiPeriod, rsiBelowDefault, minVolDefault) = mode switch
    //        {
    //            StrategyAMode.Conservative => (KlineInterval.FiveMinutes, 200, 60, 14, 28m, 0.08m),
    //            StrategyAMode.Balanced => (KlineInterval.ThreeMinutes, 160, 35, 14, 35m, 0.10m),
    //            StrategyAMode.Aggressive => (KlineInterval.OneMinute, 160, 20, 14, 45m, 0.12m),
    //            StrategyAMode.Scalping => (KlineInterval.OneMinute, 220, 12, 14, 50m, 0.05m),
    //            _ => (KlineInterval.ThreeMinutes, 160, 35, 14, 35m, 0.10m)
    //        };

    //        // Use settings overrides (UI changes them per mode)
    //        var rsiBelow = state.Settings.EntryRsiBelow > 0 ? state.Settings.EntryRsiBelow : rsiBelowDefault;
    //        var minVolPct = state.Settings.MinVolatilityPct > 0 ? state.Settings.MinVolatilityPct : minVolDefault;

    //        var closes = await GetClosesCachedAsync(cfg.Symbol, interval, limit, refreshSec, ct);
    //        if (closes.Count < 30)
    //            continue;

    //        // Indicators
    //        var rsi = Indicators.Rsi(closes, rsiPeriod);
    //        var ema9 = Indicators.Ema(closes, 9);
    //        var ema21 = Indicators.Ema(closes, 21);
    //        var trendOk = ema9 > ema21;

    //        // Volatility: last N closes range %
    //        var hi = closes.TakeLast(30).Max();
    //        var lo = closes.TakeLast(30).Min();
    //        var volPct = (lo > 0) ? ((hi - lo) / lo) * 100m : 0m;

    //        // Momentum: last close above EMA9 by small %
    //        var lastClose = closes[^1];
    //        var momPct = (ema9 > 0) ? ((lastClose - ema9) / ema9) * 100m : 0m;

    //        bool entryOk =
    //            trendOk &&
    //            (rsi <= rsiBelow) &&
    //            (volPct >= minVolPct);

    //        if (!entryOk)
    //        {
    //            state.AddLog("DEBUG",
    //                $"NO-ENTRY {cfg.Symbol} | Mode={mode} RSI={rsi:0.0}<={rsiBelow} Trend={trendOk} Vol%={volPct:0.000}>= {minVolPct}");
    //            continue;
    //        }

    //        var usdt = state.Settings.MaxUsdPerTrade;
    //        if (usdt <= 0) continue;

    //        // SL/TP (mode tweaks for scalping)
    //        var slPct = state.Settings.StopLossPct;
    //        var tpPct = state.Settings.TakeProfitPct;

    //        if (mode == StrategyAMode.Scalping)
    //        {
    //            slPct = Math.Max(0.25m, slPct * 0.60m);
    //            tpPct = Math.Max(0.35m, tpPct * 0.60m);
    //        }

    //        var sl = lastPrice * (1 - slPct / 100m);
    //        var tp = lastPrice * (1 + tpPct / 100m);

    //        // LIVE vs PAPER
    //        if (!state.Settings.PaperTrading)
    //        {
    //            state.AddLog("INFO", $"LIVE SIGNAL {cfg.Symbol} | Mode={mode} RSI={rsi:0.0} Vol%={volPct:0.000} usdt={usdt:0.00}");

    //            var buy = await _exec.MarketBuyAsync(cfg.Symbol, usdt, ct);
    //            if (!buy.Success)
    //            {
    //                state.AddLog("ERROR", $"LIVE BUY FAILED {cfg.Symbol}: {buy.Error}");
    //                state.SetCooldown(cfg.Symbol, 1);
    //                continue;
    //            }

    //            // estimate qty for tracking (Phase 2: pull executed qty)
    //            var qty = usdt / lastPrice;

    //            state.OpenPositions.Add(new PaperPosition
    //            {
    //                Symbol = cfg.Symbol,
    //                EntryPrice = lastPrice,
    //                Quantity = qty,
    //                StopLoss = sl,
    //                TakeProfit = tp,
    //                TrailingEnabled = state.Settings.EnableTrailingStop
    //            });

    //            state.AddLog("TRADE", $"LIVE BUY {cfg.Symbol} usdt={usdt:0.00} estQty={qty:0.########} orderId={buy.OrderId}");

    //            // OCO
    //            var stopLimit = sl * 0.999m;
    //            var oco = await _exec.PlaceOcoSellAsync(cfg.Symbol, qty, tp, sl, stopLimit, ct);
    //            if (!oco.Success)
    //                state.AddLog("ERROR", $"LIVE OCO FAILED {cfg.Symbol}: {oco.Error}");
    //            else
    //                state.AddLog("TRADE", $"LIVE OCO {cfg.Symbol} tp={tp:0.########} sl={sl:0.########} ocoOrderId={oco.OrderListId}");

    //            // cooldown per mode
    //            var cd = mode == StrategyAMode.Scalping ? 1 : state.Settings.SymbolCooldownMinutes;
    //            state.SetCooldown(cfg.Symbol, cd);
    //        }
    //        else
    //        {
    //            var qty = usdt / lastPrice;

    //            state.OpenPositions.Add(new PaperPosition
    //            {
    //                Symbol = cfg.Symbol,
    //                EntryPrice = lastPrice,
    //                Quantity = qty,
    //                StopLoss = sl,
    //                TakeProfit = tp,
    //                TrailingEnabled = state.Settings.EnableTrailingStop
    //            });

    //            state.AddLog("TRADE", $"PAPER BUY {cfg.Symbol} Mode={mode} @ {lastPrice:0.########} qty={qty:0.########}");

    //            var cd = mode == StrategyAMode.Scalping ? 1 : state.Settings.SymbolCooldownMinutes;
    //            state.SetCooldown(cfg.Symbol, cd);
    //        }
    //    }

    //    // EXIT tracking
    //    foreach (var pos in state.OpenPositions.Where(p => !p.IsClosed))
    //    {
    //        if (!market.Prices.TryGetValue(pos.Symbol, out var price))
    //            continue;

    //        if (price <= 0) continue;

    //        if (state.Settings.EnableBreakeven)
    //            pos.TryApplyBreakeven(price, state.Settings.BreakevenAtPct);

    //        if (state.Settings.EnableTrailingStop)
    //            pos.UpdateTrailing(price, state.Settings.TrailingStopPct);

    //        if (price <= pos.StopLoss)
    //        {
    //            state.RegisterLoss(pos, price);
    //            state.AddLog("TRADE", $"EXIT SL/TRAIL {pos.Symbol} @ {price:0.########}");
    //        }
    //        else if (price >= pos.TakeProfit)
    //        {
    //            state.RegisterWin(pos, price);
    //            state.AddLog("TRADE", $"EXIT TP {pos.Symbol} @ {price:0.########}");
    //        }
    //    }
    }

    private async Task<IReadOnlyList<decimal>> GetClosesCachedAsync(
        string symbol, KlineInterval interval, int limit, int refreshSeconds, CancellationToken ct)
    {
        var key = $"{symbol}:{interval}:{limit}";
        var now = DateTimeOffset.UtcNow;

        if (_cache.TryGetValue(key, out var cached))
        {
            if ((now - cached.UpdatedAtUtc).TotalSeconds < refreshSeconds && cached.Closes.Count > 0)
                return cached.Closes;
        }

        var closes = (await _klines.GetClosesAsync(symbol, interval, limit, ct)).ToList();
        _cache[key] = new KlineCache { UpdatedAtUtc = now, Closes = closes };
        return closes;
    }
}
