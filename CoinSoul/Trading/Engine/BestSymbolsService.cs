using Binance.Net.Interfaces.Clients;
using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Core;

public sealed class BestSymbolsService : IBestSymbolsService
{
    private readonly IBinanceRestClient _client;

    public BestSymbolsService(IBinanceRestClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyList<string>> GetBestSymbolsAsync(
        StrategyAMode mode,
        int count,
        CancellationToken ct)
    {
        var res = await _client.SpotApi.ExchangeData.GetTickersAsync(ct: ct);
        if (!res.Success || res.Data is null)
            return Array.Empty<string>();

        // إعدادات حسب الـ Mode
        decimal minVolume;
        decimal minChange;

        switch (mode)
        {
            case StrategyAMode.Conservative:
                minVolume = 50_000_000m;
                minChange = 1.0m;
                break;

            case StrategyAMode.Balanced:
                minVolume = 20_000_000m;
                minChange = 1.5m;
                break;

            case StrategyAMode.Aggressive:
                minVolume = 10_000_000m;
                minChange = 2.0m;
                break;

            case StrategyAMode.Scalping:
                minVolume = 5_000_000m;
                minChange = 0.8m;
                break;

            default:
                minVolume = 20_000_000m;
                minChange = 1.5m;
                break;
        }

        var best = res.Data
            .Where(t =>
                t.Symbol.EndsWith("USDT") &&
                t.QuoteVolume >= minVolume &&
                Math.Abs(t.PriceChangePercent) >= minChange &&
                !IsLeveraged(t.Symbol))
            .OrderByDescending(t =>
                Math.Abs(t.PriceChangePercent) * 0.6m +
                (t.QuoteVolume / 1_000_000m) * 0.4m)
            .Take(count)
            .Select(t => t.Symbol)
            .ToList();

        return best;
    }

    private static bool IsLeveraged(string symbol)
    {
        string[] bad =
        {
            "UPUSDT","DOWNUSDT","BULLUSDT","BEARUSDT",
            "3LUSDT","3SUSDT","5LUSDT","5SUSDT"
        };

        return bad.Any(b => symbol.EndsWith(b));
    }
}
