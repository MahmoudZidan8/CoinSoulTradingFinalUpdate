namespace CoinSoul.Trading.Engine.Cache;

public sealed class CachedEntry<T>
{
    public T Data { get; init; }
    public DateTime CachedAtUtc { get; init; }
    public int TtlMs { get; init; }

    public CachedEntry(T data, int ttlMs)
    {
        Data = data;
        CachedAtUtc = DateTime.UtcNow;
        TtlMs = ttlMs;
    }

    public bool IsExpired()
    {
        return DateTime.UtcNow > CachedAtUtc.AddMilliseconds(TtlMs);
    }

    public int AgeMs()
    {
        return (int)(DateTime.UtcNow - CachedAtUtc).TotalMilliseconds;
    }
}