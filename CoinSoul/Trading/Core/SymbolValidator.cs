using Binance.Net.Interfaces.Clients;

namespace CoinSoul.Trading.Core;

public interface ISymbolValidator
{
    Task<bool> ExistsAsync(string symbol, CancellationToken ct);
}

public sealed class BinanceSymbolValidator : ISymbolValidator
{
    private readonly IBinanceRestClient _client;
    private static HashSet<string>? _cache;
    private static DateTime _cacheAtUtc;
    private static readonly SemaphoreSlim _gate = new(1,1);

    public BinanceSymbolValidator(IBinanceRestClient client)
    {
        _client = client;
    }

    public async Task<bool> ExistsAsync(string symbol, CancellationToken ct)
    {
        if (_cache is not null && _cacheAtUtc.AddHours(6) > DateTime.UtcNow)
            return _cache.Contains(symbol);

        await _gate.WaitAsync(ct);
        try
        {
            if (_cache is not null && _cacheAtUtc.AddHours(6) > DateTime.UtcNow)
                return _cache.Contains(symbol);

            var info = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync(ct: ct);
            if (!info.Success || info.Data is null)
                return false;

            _cache = info.Data.Symbols
                .Where(s => s.Status == Binance.Net.Enums.SymbolStatus.Trading)
                .Select(s => s.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _cacheAtUtc = DateTime.UtcNow;
            return _cache.Contains(symbol);
        }
        finally
        {
            _gate.Release();
        }
    }
}
