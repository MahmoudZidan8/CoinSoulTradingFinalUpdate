CoinSoul AI v16 SCALP INTELLIGENCE STABLE

Built on top of v15 cleanbuild.
Speed/stability fixes:
- Prefilter widened to top 120, deep scan reduced to top 20
- Deep scan concurrency capped at 4
- Symbol validator exchangeInfo cache hardened (6h + gate)
- Trade sync interval increased to 10 minutes
- Active trade sync symbols filtered against valid USDT market list to avoid invalid symbols
- API budget default reduced to 600

Recommended defaults:
Trade size=18
Max positions=20
Queue size=100
Deep scan=20
