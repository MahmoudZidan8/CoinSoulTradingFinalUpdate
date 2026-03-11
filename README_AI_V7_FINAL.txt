CoinSoul AI v7 FINAL source hotfix

What changed:
- OCO prices and quantities are now normalized to exact exchange precision based on tickSize/stepSize.
- stopLimit is forced below stop by multiple ticks to satisfy Binance OCO rules.
- OCO failure now logs the actual Binance rejection reason after retries.
- Fallback TP/SL uses exchange-normalized quantity and prices.

Notes:
- This is a source patch bundle. Build and test in your local environment.
