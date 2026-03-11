using CoinSoul.Entities;
using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine.Settings;

public static class BotSettingsMapping
{
    public static BotSettings ToCoreSettings(this BotSettingsEntity e)
    {
        return new BotSettings
        {
            TradeMode = e.TradeMode.Equals("Futures", StringComparison.OrdinalIgnoreCase)
                ? TradeMode.Futures
                : TradeMode.Spot,

            StrategyMode = StrategyMode.AutoScalperD,

            AutoScalperEnabled = e.AutoScalperEnabled,
            PaperTrading = e.PaperTrading,

            NetProfitTargetUsd = e.NetProfitTargetUsd,
            MaxTradeDurationSeconds = e.MaxTradeDurationSeconds,
            HardStopLossPct = e.HardStopLossPct,
            MaxSpreadPct = e.MaxSpreadPct,
            Min24hQuoteVolumeUsdt = e.Min24hQuoteVolumeUsdt,
            SlippageBufferUsd = e.SlippageBufferUsd,

            MaxUsdPerTrade = e.TradeSizeUsd,
            MaxOpenTrades = e.MaxConcurrentPositions,

            TickSeconds = e.TickSeconds,
            TimeExitMinutes = e.TimeExitMinutes,

            UseOcoExit = e.UseOcoExit,
            OcoStopLimitBufferPct = e.OcoStopLimitBufferPct,
            QueueSize = e.QueueSize,
            DeepScanTopN = e.DeepScanTopN,
            TierAConfidenceThreshold = e.TierAConfidenceThreshold,
            TierBConfidenceThreshold = e.TierBConfidenceThreshold,
            TierCConfidenceThreshold = e.TierCConfidenceThreshold,
            ExpectedNetAfterFeesUsd = e.ExpectedNetAfterFeesUsd,
            OpportunitySwitchHoldMinutes = e.OpportunitySwitchHoldMinutes,
            OpportunitySwitchMinConfidenceGap = e.OpportunitySwitchMinConfidenceGap,
            SoftReviewMinutes1 = e.SoftReviewMinutes1,
            SoftReviewMinutes2 = e.SoftReviewMinutes2,
            FinalEntryMaxSpreadPct = e.FinalEntryMaxSpreadPct,
            FinalEntryMinOrderbookImbalance = e.FinalEntryMinOrderbookImbalance,
            FinalEntryMinMomentumPct = e.FinalEntryMinMomentumPct,
            ApiBudgetPerMinute = e.ApiBudgetPerMinute
        };
    }
}