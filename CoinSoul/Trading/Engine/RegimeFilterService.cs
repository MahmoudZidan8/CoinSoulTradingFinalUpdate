using Binance.Net.Interfaces.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public sealed class RegimeFilterService
{
    private readonly IBinanceRestClient _client;
    private readonly CoinSoulDbContext _db;
    private static readonly Dictionary<string, DateTime> _symbolCooldowns = new();

    public RegimeFilterService(IBinanceRestClient client, CoinSoulDbContext db)
    {
        _client = client;
        _db = db;
    }

    public async Task<bool> IsMarketHealthyAsync(CancellationToken ct)
    {
        try
        {
            var btcTrend = await CheckBtcTrendAsync(ct);
            if (!btcTrend) return false;

            var volatility = await CheckVolatilityAsync(ct);
            if (!volatility) return false;

            return true;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool CanTrade, string Reason)> CanTradeSymbolAsync(string symbol, CancellationToken ct)
    {
        if (_symbolCooldowns.TryGetValue(symbol, out var cooldownUntil))
        {
            if (DateTime.UtcNow < cooldownUntil)
            {
                return (false, "Symbol in cooldown");
            }
            
            _symbolCooldowns.Remove(symbol);
        }

        var (spreadOk, spread) = await CheckSpreadAsync(symbol, ct);
        if (!spreadOk)
        {
            return (false, $"Spread too high: {spread:0.00}%");
        }

        var volumeOk = await CheckVolumeAsync(symbol, ct);
        if (!volumeOk)
        {
            return (false, "Insufficient volume");
        }

        return (true, "Passed");
    }

    public void SetSymbolCooldown(string symbol, int minutes)
    {
        _symbolCooldowns[symbol] = DateTime.UtcNow.AddMinutes(minutes);
    }

    private async Task<bool> CheckBtcTrendAsync(CancellationToken ct)
    {
        var klines = await _client.SpotApi.ExchangeData.GetKlinesAsync(
            "BTCUSDT", 
            KlineInterval.OneHour, 
            limit: 200, 
            ct: ct);

        if (!klines.Success || klines.Data == null) return false;

        var closes = klines.Data.Select(k => k.ClosePrice).ToList();
        
        var ema50 = CalculateEMA(closes, 50);
        var ema200 = CalculateEMA(closes, 200);

        return ema50 > ema200;
    }

    private async Task<bool> CheckVolatilityAsync(CancellationToken ct)
    {
        var klines = await _client.SpotApi.ExchangeData.GetKlinesAsync(
            "BTCUSDT", 
            KlineInterval.OneHour, 
            limit: 20, 
            ct: ct);

        if (!klines.Success || klines.Data == null) return false;

        var data = klines.Data.ToList();
        var atr = CalculateATR(data);
        var avgAtr = data.Average(k => (k.HighPrice - k.LowPrice));

        return atr > avgAtr * 0.5m && atr < avgAtr * 2.0m;
    }

    private async Task<(bool Ok, decimal Spread)> CheckSpreadAsync(string symbol, CancellationToken ct)
    {
        var book = await _client.SpotApi.ExchangeData.GetBookPriceAsync(symbol, ct);
        
        if (!book.Success || book.Data == null) return (false, 0);

        var spread = ((book.Data.BestAskPrice - book.Data.BestBidPrice) / book.Data.BestBidPrice) * 100m;
        
        return (spread <= 0.15m, spread);
    }

    private async Task<bool> CheckVolumeAsync(string symbol, CancellationToken ct)
    {
        var klines = await _client.SpotApi.ExchangeData.GetKlinesAsync(
            symbol, 
            KlineInterval.FiveMinutes, 
            limit: 20, 
            ct: ct);

        if (!klines.Success || klines.Data == null) return false;

        var data = klines.Data.ToList();
        var currentVolume = data.Last().Volume;
        var avgVolume = data.Average(k => k.Volume);

        return currentVolume > avgVolume * 1.2m;
    }

    private static decimal CalculateEMA(List<decimal> values, int period)
    {
        if (values.Count < period) return 0;

        var multiplier = 2m / (period + 1);
        var ema = values.Take(period).Average();

        foreach (var value in values.Skip(period))
        {
            ema = ((value - ema) * multiplier) + ema;
        }

        return ema;
    }

    private static decimal CalculateATR(List<IBinanceKline> klines)
    {
        if (klines.Count < 2) return 0;

        var trs = new List<decimal>();
        
        for (int i = 1; i < klines.Count; i++)
        {
            var high = klines[i].HighPrice;
            var low = klines[i].LowPrice;
            var prevClose = klines[i - 1].ClosePrice;

            var tr = Math.Max(high - low, 
                     Math.Max(Math.Abs(high - prevClose), 
                     Math.Abs(low - prevClose)));
            
            trs.Add(tr);
        }

        return trs.Average();
    }
}