CoinSoul AI v9 Institutional Level Engine

Patched items:
- Added TryRefreshPortfolioAfterProtectionAsync to PrecisionTradeExecutor
- Added ILogger injection to BinanceTradeExecutor (fix _logger compile issue)
- Stronger OCO stopLimit normalization to guarantee stopLimit < stop
- Added explicit OCO_FAILED stage/event logging before fallback
- Balance refresh after protection only
