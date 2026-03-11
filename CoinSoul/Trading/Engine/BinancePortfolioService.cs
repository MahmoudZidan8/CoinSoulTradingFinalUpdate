using CoinSoul.Entities;
using CoinSoul.Trading.Core;
using CoinSoul.Repository.DbContext;
using Binance.Net.Interfaces.Clients;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Binance implementation of IPortfolioService
/// </summary>
public sealed class BinancePortfolioService : IPortfolioService
{
    private readonly IBinanceRestClient _client;
    private readonly CoinSoulDbContext _db;

    public BinancePortfolioService(IBinanceRestClient client, CoinSoulDbContext db)
    {
        _client = client;
        _db = db;
    }

    public async Task<PortfolioDto> GetPortfolioAsync(CancellationToken ct = default)
    {
        try
        {
            var accountInfo = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            
            if (!accountInfo.Success || accountInfo.Data == null)
            {
                return new PortfolioDto();
            }

            var balances = accountInfo.Data.Balances.Where(b => b.Total > 0).ToList();
            
            if (!balances.Any())
            {
                return new PortfolioDto();
            }

            var tickers = await _client.SpotApi.ExchangeData.GetTickersAsync(ct: ct);
            var priceMap = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

            if (tickers.Success && tickers.Data != null)
            {
                foreach (var ticker in tickers.Data)
                {
                    if (ticker.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
                    {
                        var baseAsset = ticker.Symbol[..^4];
                        priceMap[baseAsset] = ticker.LastPrice;
                    }
                }
            }

            decimal totalEquityUsdt = 0;
            decimal freeUsdt = 0;
            decimal lockedUsdt = 0;
            var holdings = new List<PortfolioHoldingDto>();

            foreach (var balance in balances)
            {
                var asset = balance.Asset;
                var free = balance.Available;
                var locked = balance.Locked;
                var total = balance.Total;

                if (asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                {
                    freeUsdt = free;
                    lockedUsdt = locked;
                    totalEquityUsdt += total;
                    
                    holdings.Add(new PortfolioHoldingDto
                    {
                        Asset = asset,
                        Free = free,
                        Locked = locked,
                        UsdtValue = total
                    });
                }
                else if (priceMap.TryGetValue(asset, out var priceUsdt))
                {
                    var usdtValue = total * priceUsdt;
                    totalEquityUsdt += usdtValue;
                    
                    holdings.Add(new PortfolioHoldingDto
                    {
                        Asset = asset,
                        Free = free,
                        Locked = locked,
                        UsdtValue = usdtValue
                    });
                }
            }

            var startOfDay = await GetStartOfDayEquityAsync(ct);

            return new PortfolioDto
            {
                TotalEquityUsdt = totalEquityUsdt,
                FreeUsdt = freeUsdt,
                LockedUsdt = lockedUsdt,
                StartOfDayEquityUsdt = startOfDay,
                Holdings = holdings.OrderByDescending(h => h.UsdtValue).Take(10).ToList()
            };
        }
        catch
        {
            return new PortfolioDto();
        }
    }

    private async Task<decimal> GetStartOfDayEquityAsync(CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var snapshot = await _db.EquitySnapshotEntity
            .Where(s => s.DayUtc == today)
            .OrderBy(s => s.AtUtc)
            .FirstOrDefaultAsync(ct);

        if (snapshot != null)
        {
            return snapshot.StartOfDayEquityUsdt;
        }

        var settings = await _db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        return settings?.EquityStartOfDayUsdt ?? 0;
    }
}