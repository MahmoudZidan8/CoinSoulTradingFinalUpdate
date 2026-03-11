using CoinSoul.Entities;
using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public static class BotSettingsMapping
{
    public static BotSettings ToDomain(this BotSettingsEntity e)
    {
        return new BotSettings
        {
            TradeMode = e.TradeMode.Equals("Futures", StringComparison.OrdinalIgnoreCase)
                ? TradeMode.Futures : TradeMode.Spot,

            StrategyMode = (StrategyMode)e.StrategyModeValue,

            AutoScalperEnabled = e.AutoScalperEnabled,
            NetProfitTargetUsd = e.NetProfitTargetUsd,
            MaxTradeDurationSeconds = e.MaxTradeDurationSeconds,
            HardStopLossPct = e.HardStopLossPct,
            MaxSpreadPct = e.MaxSpreadPct,
            Min24hQuoteVolumeUsdt = e.Min24hQuoteVolumeUsdt,
            SlippageBufferUsd = e.SlippageBufferUsd,

            MaxUsdPerTrade = e.MaxUsdPerTrade,
            PaperTrading = e.PaperTrading,
            TickSeconds = e.TickSeconds,
            TimeExitMinutes = e.TimeExitMinutes
        };
    }

    public static void ApplyFromDomain(this BotSettingsEntity e, BotSettings s)
    {
        e.TradeMode = s.TradeMode == TradeMode.Futures ? "Futures" : "Spot";
        e.StrategyModeValue = (int)s.StrategyMode;

        e.AutoScalperEnabled = s.AutoScalperEnabled;
        e.NetProfitTargetUsd = s.NetProfitTargetUsd;
        e.MaxTradeDurationSeconds = s.MaxTradeDurationSeconds;
        e.HardStopLossPct = s.HardStopLossPct;
        e.MaxSpreadPct = s.MaxSpreadPct;
        e.Min24hQuoteVolumeUsdt = s.Min24hQuoteVolumeUsdt;
        e.SlippageBufferUsd = s.SlippageBufferUsd;

        e.MaxUsdPerTrade = s.MaxUsdPerTrade;
        e.PaperTrading = s.PaperTrading;
        e.TickSeconds = s.TickSeconds;
        e.TimeExitMinutes = s.TimeExitMinutes;
    }
}
