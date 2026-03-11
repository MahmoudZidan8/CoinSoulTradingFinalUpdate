CoinSoul AI v6 STABLE

Patched from v5 source package.

Changes:
- Prevent regime risk multiplier from shrinking trade size below TargetUsdPerTrade (18 USDT stays 18 even in BearTrend).
- HybridEntryService clamps target USD to at least TargetUsdPerTrade before precision rounding.
- Added SQL settings patch that only updates columns that exist in dbo.BotSettings.

Important:
- This is a patched source package. I could not run dotnet build in this environment because .NET SDK is unavailable here.
