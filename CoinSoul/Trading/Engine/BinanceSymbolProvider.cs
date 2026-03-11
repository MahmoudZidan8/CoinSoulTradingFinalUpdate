using Binance.Net;
using Binance.Net.Clients;
using CryptoExchange.Net.Authentication;
using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public sealed class BinanceSymbolProvider : ISymbolProvider
{
    private readonly BinanceRestClient _client;

    public BinanceSymbolProvider(IConfiguration config)
    {
        _client = new BinanceRestClient(options =>
        {
            options.ApiCredentials = new ApiCredentials(
                config["Binance:ApiKey"],
                config["Binance:SecretKey"]
            );
        });
    }

    public async Task<List<SymbolInfo>> GetSpotSymbolsAsync()
    {
        var result = await _client.SpotApi.ExchangeData.GetExchangeInfoAsync();

        if (!result.Success || result.Data == null)
            return new();

        return result.Data.Symbols
            .Where(s =>
                s.Status == Binance.Net.Enums.SymbolStatus.Trading &&
                s.QuoteAsset == "USDT")
            .Select(s => new SymbolInfo(
                s.Name,
                s.BaseAsset,
                s.QuoteAsset))
            .OrderBy(s => s.Symbol)
            .ToList();
    }
}
