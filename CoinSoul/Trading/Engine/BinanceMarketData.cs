using Binance.Net.Clients;

namespace CoinSoul.Trading.Engine;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;

public interface IMarketDataProvider
{
    Task<Dictionary<string, decimal>> GetLastPricesAsync(IEnumerable<string> symbols, CancellationToken ct);
}

public sealed class BinanceMarketDataProvider(IBinanceRestClient binance) : IMarketDataProvider
{
    private readonly IBinanceRestClient _binance = binance;

    public async Task<Dictionary<string, decimal>> GetLastPricesAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        var list = symbols
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToUpperInvariant())
            .Distinct()
            .ToList();

        var result = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var sym in list)
        {
            var priceRes = await _binance.SpotApi.ExchangeData.GetPriceAsync(sym, ct);
            if (priceRes.Success && priceRes.Data is not null)
                result[sym] = priceRes.Data.Price;
        }

        return result;
    }
}
