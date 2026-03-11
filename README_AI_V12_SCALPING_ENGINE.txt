CoinSoul AI v12 SCALPING ENGINE

This package is built on top of the v11 source package.

Included source-level updates:
- Market scan interval default = 5 seconds
- Queue size default = Top 100 coins
- Queue size can be configured from configuration keys: Trading:QueueSize or QueueSize
- Default trade sizing aligned to your requested setup: 18 USDT
- Default max open / concurrent positions aligned to high-throughput mode: 20
- Limit-first timeout default = 20 seconds
- Smart cooldown default = 10 minutes

Target operating model:
- USDT pairs only
- scanner always running
- ranked queue refreshed every 5 seconds
- top 100 candidate queue kept ready for instant execution

Important:
This is still a source patch package and should be built/tested in your local .NET environment before live use.
