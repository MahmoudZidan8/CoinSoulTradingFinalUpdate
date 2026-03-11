UPDATE dbo.BotSettings
SET
    TargetUsdPerTrade = 18,
    MinUsdPerTrade = 18,
    TradeHistoryTopSymbols = 100,
    MaxOpenTrades = 20,
    MaxConcurrentPositions = 20,
    TakeProfitGrossPct = 1.0,
    StopLossGrossPct = 2.0,
    NetProfitTargetUsd = 0.01,
    LimitMakerTimeoutSeconds = 20,
    SmartCooldownMinutes = 10
WHERE Id = (SELECT TOP 1 Id FROM dbo.BotSettings ORDER BY Id);
