using Binance.Net.Enums;
using CoinSoul.Trading.Engine.Cache;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Adaptive;

public sealed class VolatilityCalculator : IVolatilityCalculator
{
    private readonly IMarketDataCache _cache;
    private readonly ILogger<VolatilityCalculator> _logger;

    public VolatilityCalculator(
        IMarketDataCache cache,
        ILogger<VolatilityCalculator> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<decimal> CalculateVolatilityAsync(string symbol, CancellationToken ct)
    {
        try
        {
            // Get recent 1-minute klines
            var klineData = await _cache.GetOrFetchKlinesAsync(
                symbol,
                KlineInterval.OneMinute,
                60,
                ct);

            if (klineData == null || klineData.Closes.Count < 14)
                return 0m;

            // Calculate ATR-like volatility
            var ranges = new List<decimal>();
            for (int i = 0; i < Math.Min(14, klineData.Highs.Count); i++)
            {
                var range = klineData.Highs[i] - klineData.Lows[i];
                ranges.Add(range);
            }

            var avgRange = ranges.Average();
            var lastPrice = klineData.Closes[^1];

            if (lastPrice == 0)
                return 0m;

            var volatilityPct = (avgRange / lastPrice) * 100m;
            return volatilityPct;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VOLATILITY] Error calculating for {Symbol}", symbol);
            return 0m;
        }
    }

    public async Task<decimal> GetMarketVolatilityAsync(CancellationToken ct)
    {
        try
        {
            // Use BTC as market proxy
            return await CalculateVolatilityAsync("BTCUSDT", ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[VOLATILITY] Error calculating market volatility");
            return 0m;
        }
    }
}