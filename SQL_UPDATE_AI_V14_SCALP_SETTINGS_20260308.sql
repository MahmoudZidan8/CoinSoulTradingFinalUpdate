UPDATE BotSettings
SET TargetUsdPerTrade = 18,
    MinUsdPerTrade = 18,
    MaxOpenTrades = 20,
    TradeHistoryTopSymbols = 100,
    LimitMakerTimeoutSeconds = 20,
    SmartCooldownMinutes = 10,
    UseOcoExit = 1,
    TakeProfitGrossPct = 1.00,
    StopLossGrossPct = 2.00
;
