using System.Collections.Concurrent;

namespace CoinSoul.Trading.Engine.Cache;

public sealed record CacheStats(
    int CacheHits,
    int CacheMisses,
    double HitRatePercent,
    int BookTickerCount,
    int KlineCount,
    int StreamHits,
    int RestFallbacks,
    double StreamHitRatePercent);