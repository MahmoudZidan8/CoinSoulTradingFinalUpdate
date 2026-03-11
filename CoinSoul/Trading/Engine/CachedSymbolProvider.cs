using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public sealed class CachedSymbolProvider : ISymbolProvider
{
    private readonly ISymbolProvider _inner;

    private readonly object _lock = new();
    private List<SymbolInfo> _cache = new();
    private DateTimeOffset _lastFetch = DateTimeOffset.MinValue;

    // مدة صلاحية الكاش (ممكن تغيرها بعدين)
    private static readonly TimeSpan Ttl = TimeSpan.FromHours(6);

    public CachedSymbolProvider(BinanceSymbolProvider inner)
    {
        _inner = inner;
    }

    public async Task<List<SymbolInfo>> GetSpotSymbolsAsync()
    {
        // سريعًا: رجّع الكاش لو صالح
        lock (_lock)
        {
            if (_cache.Count > 0 && (DateTimeOffset.UtcNow - _lastFetch) < Ttl)
                return _cache;
        }

        // لو مش صالح: هات من المصدر
        var fresh = await _inner.GetSpotSymbolsAsync();

        lock (_lock)
        {
            _cache = fresh ?? new List<SymbolInfo>();
            _lastFetch = DateTimeOffset.UtcNow;
            return _cache;
        }
    }
}
