using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;

namespace CoinSoul.Trading.Core;

public interface IMarketKlineProvider
{
    Task<IReadOnlyList<decimal>> GetClosesAsync(string symbol, KlineInterval interval, int limit, CancellationToken ct);
}

public sealed class MarketKlineProvider : IMarketKlineProvider
{
    private readonly IBinanceRestClient _client;

    public MarketKlineProvider(IBinanceRestClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<decimal>> GetClosesAsync(string symbol, KlineInterval interval, int limit, CancellationToken ct)
    {
        var res = await _client.SpotApi.ExchangeData.GetKlinesAsync(symbol, interval, limit: limit, ct: ct);
        if (!res.Success || res.Data is null)
            return Array.Empty<decimal>();

        return res.Data.Select(k => k.ClosePrice).ToList();
    }
}
