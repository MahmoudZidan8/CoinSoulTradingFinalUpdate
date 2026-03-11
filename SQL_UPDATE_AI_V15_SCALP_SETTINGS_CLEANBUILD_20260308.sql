IF COL_LENGTH('dbo.BotSettings', 'QueueSize') IS NULL
    ALTER TABLE dbo.BotSettings ADD QueueSize INT NULL;
IF COL_LENGTH('dbo.BotSettings', 'DeepScanTopN') IS NULL
    ALTER TABLE dbo.BotSettings ADD DeepScanTopN INT NULL;
IF COL_LENGTH('dbo.BotSettings', 'TierAConfidenceThreshold') IS NULL
    ALTER TABLE dbo.BotSettings ADD TierAConfidenceThreshold DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'TierBConfidenceThreshold') IS NULL
    ALTER TABLE dbo.BotSettings ADD TierBConfidenceThreshold DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'TierCConfidenceThreshold') IS NULL
    ALTER TABLE dbo.BotSettings ADD TierCConfidenceThreshold DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'ExpectedNetAfterFeesUsd') IS NULL
    ALTER TABLE dbo.BotSettings ADD ExpectedNetAfterFeesUsd DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'OpportunitySwitchHoldMinutes') IS NULL
    ALTER TABLE dbo.BotSettings ADD OpportunitySwitchHoldMinutes INT NULL;
IF COL_LENGTH('dbo.BotSettings', 'OpportunitySwitchMinConfidenceGap') IS NULL
    ALTER TABLE dbo.BotSettings ADD OpportunitySwitchMinConfidenceGap DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'SoftReviewMinutes1') IS NULL
    ALTER TABLE dbo.BotSettings ADD SoftReviewMinutes1 INT NULL;
IF COL_LENGTH('dbo.BotSettings', 'SoftReviewMinutes2') IS NULL
    ALTER TABLE dbo.BotSettings ADD SoftReviewMinutes2 INT NULL;
IF COL_LENGTH('dbo.BotSettings', 'FinalEntryMaxSpreadPct') IS NULL
    ALTER TABLE dbo.BotSettings ADD FinalEntryMaxSpreadPct DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'FinalEntryMinOrderbookImbalance') IS NULL
    ALTER TABLE dbo.BotSettings ADD FinalEntryMinOrderbookImbalance DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'FinalEntryMinMomentumPct') IS NULL
    ALTER TABLE dbo.BotSettings ADD FinalEntryMinMomentumPct DECIMAL(18,8) NULL;
IF COL_LENGTH('dbo.BotSettings', 'ApiBudgetPerMinute') IS NULL
    ALTER TABLE dbo.BotSettings ADD ApiBudgetPerMinute DECIMAL(18,8) NULL;

UPDATE dbo.BotSettings
SET TargetUsdPerTrade = 18,
    MinUsdPerTrade = 18,
    MaxOpenTrades = 20,
    TradeHistoryTopSymbols = 100,
    QueueSize = COALESCE(QueueSize, 100),
    DeepScanTopN = COALESCE(DeepScanTopN, 30),
    TierAConfidenceThreshold = COALESCE(TierAConfidenceThreshold, 0.90),
    TierBConfidenceThreshold = COALESCE(TierBConfidenceThreshold, 0.80),
    TierCConfidenceThreshold = COALESCE(TierCConfidenceThreshold, 0.70),
    ExpectedNetAfterFeesUsd = COALESCE(ExpectedNetAfterFeesUsd, 0.01),
    OpportunitySwitchHoldMinutes = COALESCE(OpportunitySwitchHoldMinutes, 30),
    OpportunitySwitchMinConfidenceGap = COALESCE(OpportunitySwitchMinConfidenceGap, 0.08),
    SoftReviewMinutes1 = COALESCE(SoftReviewMinutes1, 5),
    SoftReviewMinutes2 = COALESCE(SoftReviewMinutes2, 15),
    FinalEntryMaxSpreadPct = COALESCE(FinalEntryMaxSpreadPct, 0.12),
    FinalEntryMinOrderbookImbalance = COALESCE(FinalEntryMinOrderbookImbalance, 1.05),
    FinalEntryMinMomentumPct = COALESCE(FinalEntryMinMomentumPct, 0.02),
    ApiBudgetPerMinute = COALESCE(ApiBudgetPerMinute, 720);
