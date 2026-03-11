CoinSoul AI v5 PRO

Included in this patch:
- PerformanceMetricsService DI registration fix (Performance page should resolve service)
- Safe SQL settings script using existing BotSettings column names only
- OCO settings mapped to UseOcoExit / LimitMakerTimeoutSeconds / SmartCooldownMinutes

Recommended after applying:
1. Run SQL_UPDATE_AI_V5_PRODUCTION_SETTINGS_20260307.sql
2. Build solution
3. Verify /trading/performance opens
4. Watch TradingEvents for OCO_OK / EXIT_OK
