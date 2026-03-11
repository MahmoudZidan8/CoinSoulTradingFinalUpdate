-- CoinSoul AI v6 STABLE settings patch
-- Safe against missing columns. Uses only columns that actually exist.

IF OBJECT_ID('dbo.BotSettings', 'U') IS NULL
BEGIN
    RAISERROR('dbo.BotSettings table not found.', 16, 1);
    RETURN;
END

IF COL_LENGTH('dbo.BotSettings', 'TargetUsdPerTrade') IS NOT NULL
    UPDATE dbo.BotSettings SET TargetUsdPerTrade = 18;
IF COL_LENGTH('dbo.BotSettings', 'MinUsdPerTrade') IS NOT NULL
    UPDATE dbo.BotSettings SET MinUsdPerTrade = 18;
IF COL_LENGTH('dbo.BotSettings', 'MaxOpenTrades') IS NOT NULL
    UPDATE dbo.BotSettings SET MaxOpenTrades = 10;
IF COL_LENGTH('dbo.BotSettings', 'TradeHistoryTopSymbols') IS NOT NULL
    UPDATE dbo.BotSettings SET TradeHistoryTopSymbols = 30;
IF COL_LENGTH('dbo.BotSettings', 'TakeProfitGrossPct') IS NOT NULL
    UPDATE dbo.BotSettings SET TakeProfitGrossPct = 1.00;
IF COL_LENGTH('dbo.BotSettings', 'StopLossGrossPct') IS NOT NULL
    UPDATE dbo.BotSettings SET StopLossGrossPct = 2.00;
IF COL_LENGTH('dbo.BotSettings', 'NetProfitTargetUsd') IS NOT NULL
    UPDATE dbo.BotSettings SET NetProfitTargetUsd = 0.20;
IF COL_LENGTH('dbo.BotSettings', 'UseOcoExit') IS NOT NULL
    UPDATE dbo.BotSettings SET UseOcoExit = 1;
IF COL_LENGTH('dbo.BotSettings', 'PlaceSeparateTpSlIfOcoFails') IS NOT NULL
    UPDATE dbo.BotSettings SET PlaceSeparateTpSlIfOcoFails = 1;
IF COL_LENGTH('dbo.BotSettings', 'LimitMakerTimeoutSeconds') IS NOT NULL
    UPDATE dbo.BotSettings SET LimitMakerTimeoutSeconds = 20;
IF COL_LENGTH('dbo.BotSettings', 'SmartCooldownMinutes') IS NOT NULL
    UPDATE dbo.BotSettings SET SmartCooldownMinutes = 10;
IF COL_LENGTH('dbo.BotSettings', 'CooldownAfterLossSeconds') IS NOT NULL
    UPDATE dbo.BotSettings SET CooldownAfterLossSeconds = 600;
IF COL_LENGTH('dbo.BotSettings', 'CooldownSameSymbolSeconds') IS NOT NULL
    UPDATE dbo.BotSettings SET CooldownSameSymbolSeconds = 600;
IF COL_LENGTH('dbo.BotSettings', 'EntryCooldownSeconds') IS NOT NULL
    UPDATE dbo.BotSettings SET EntryCooldownSeconds = 30;
IF COL_LENGTH('dbo.BotSettings', 'EnableSmartCooldown') IS NOT NULL
    UPDATE dbo.BotSettings SET EnableSmartCooldown = 1;
IF COL_LENGTH('dbo.BotSettings', 'OcoRetryAttempts') IS NOT NULL
    UPDATE dbo.BotSettings SET OcoRetryAttempts = 3;
IF COL_LENGTH('dbo.BotSettings', 'OcoStopLimitBufferPct') IS NOT NULL
    UPDATE dbo.BotSettings SET OcoStopLimitBufferPct = 0.0015;

SELECT TOP (5) * FROM dbo.BotSettings;
