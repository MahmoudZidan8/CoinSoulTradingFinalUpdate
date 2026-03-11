using Binance.Net.Interfaces.Clients;

namespace CoinSoul.Trading.Engine;

public sealed class SlippageProtection
{
    private readonly IBinanceRestClient _client;

    public SlippageProtection(IBinanceRestClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Check if current price is within acceptable slippage range for entry
    /// </summary>
    public async Task<(bool Acceptable, decimal CurrentPrice, string? Reason)> CheckEntrySlippageAsync(
        string symbol,
        decimal referencePrice,
        decimal maxSlippagePct,
        CancellationToken ct)
    {
        try
        {
            var ticker = await _client.SpotApi.ExchangeData.GetTickerAsync(symbol, ct);
            
            if (!ticker.Success || ticker.Data == null)
            {
                return (false, 0, $"Failed to get ticker: {ticker.Error?.Message}");
            }

            var currentPrice = ticker.Data.LastPrice;
            var slippagePct = Math.Abs((currentPrice - referencePrice) / referencePrice) * 100m;

            if (slippagePct > maxSlippagePct)
            {
                return (false, currentPrice, $"Slippage {slippagePct:0.00}% exceeds max {maxSlippagePct:0.00}%");
            }

            return (true, currentPrice, null);
        }
        catch (Exception ex)
        {
            return (false, 0, $"Exception: {ex.Message}");
        }
    }

    /// <summary>
    /// Get current best ask price for entry reference
    /// </summary>
    public async Task<(bool Success, decimal Price, string? Error)> GetBestAskAsync(string symbol, CancellationToken ct)
    {
        try
        {
            var book = await _client.SpotApi.ExchangeData.GetBookPriceAsync(symbol, ct);
            
            if (!book.Success || book.Data == null)
            {
                return (false, 0, book.Error?.Message);
            }

            return (true, book.Data.BestAskPrice, null);
        }
        catch (Exception ex)
        {
            return (false, 0, ex.Message);
        }
    }
}