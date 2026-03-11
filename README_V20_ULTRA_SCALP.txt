CoinSoul AI v20 ULTRA SCALP

Built on top of v19 PRO ENGINE.

Goal:
- Scan all USDT pairs quickly
- Keep queue filled
- Increase trade frequency
- Relax final filters to reduce QUEUE_EMPTY

Key defaults:
- TradeSize = 18 USDT
- MaxOpenTrades = 20
- QueueSize = 160
- DeepScanTopN = 20
- PrefilterTake = 450
- TakeTop = 160
- MaxParallelism = 8
- ExpectedNetAfterFeesUsd = 0.003
- FinalEntryMaxSpreadPct = 0.40
- FinalEntryMinOrderbookImbalance = 1.002
- FinalEntryMinMomentumPct = 0.0005
- MomentumMinPct = -0.50
- MinVolume24hUsd = 25000
- SpikeBlockAtrPct = 3.00
- SpikeBlock1mMovePct = 2.20
- ApiBudgetPerMinute = 1200

Recommended test flow:
1. Build solution
2. Update BotSettings using included SQL
3. Run bot locally for 15-30 minutes
4. Check logs for QUEUE_REFRESH / ENTRY_PENDING / ENTRY_FILLED frequency
