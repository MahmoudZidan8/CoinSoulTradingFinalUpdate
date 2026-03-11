IF COL_LENGTH('dbo.BotSettings','TopSymbolsCount') IS NOT NULL
    UPDATE dbo.BotSettings SET TopSymbolsCount = 100;

IF COL_LENGTH('dbo.BotSettings','TradeHistoryTopSymbols') IS NOT NULL
    UPDATE dbo.BotSettings SET TradeHistoryTopSymbols = 100;

IF COL_LENGTH('dbo.BotSettings','TargetUsdPerTrade') IS NOT NULL
    UPDATE dbo.BotSettings SET TargetUsdPerTrade = 18;

IF COL_LENGTH('dbo.BotSettings','MinUsdPerTrade') IS NOT NULL
    UPDATE dbo.BotSettings SET MinUsdPerTrade = 18;
