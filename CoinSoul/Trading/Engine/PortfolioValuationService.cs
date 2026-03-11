using System.Text.Json;
using Binance.Net.Interfaces.Clients;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

// ❌ DELETE - replaced by BinancePortfolioService

public sealed class PortfolioValuationService
{
    private readonly IBinanceRestClient _client;

    public PortfolioValuationService(IBinanceRestClient client)
    {
        _client = client;
    }

    public async Task<PortfolioSnapshot> GetPortfolioAsync(CancellationToken ct)
    {
        try
        {
            var accountInfo = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!accountInfo.Success || accountInfo.Data == null)
            {
                return new PortfolioSnapshot
                {
                    Success = false,
                    Error = $"Failed to get account info: {accountInfo.Error?.Message}"
                };
            }

            var balances = accountInfo.Data.Balances.Where(b => b.Total > 0).ToList();
            
            if (!balances.Any())
            {
                return new PortfolioSnapshot
                {
                    Success = true,
                    TotalEquityUsdt = 0,
                    FreeUsdt = 0,
                    LockedUsdt = 0,
                    Holdings = new List<AssetHolding>()
                };
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

            var holdings = new List<AssetHolding>();
            decimal totalEquityUsdt = 0;
            decimal freeUsdt = 0;
            decimal lockedUsdt = 0;

            foreach (var balance in balances)
            {
                var asset = balance.Asset;
                var free = balance.Available;
                var locked = balance.Locked;
                var total = balance.Total;

                decimal priceUsdt = 1m;
                
                if (!asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                {
                    if (!priceMap.TryGetValue(asset, out priceUsdt))
                    {
                        continue;
                    }
                }

                var freeValueUsdt = free * priceUsdt;
                var lockedValueUsdt = locked * priceUsdt;
                var totalValueUsdt = total * priceUsdt;

                if (asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                {
                    freeUsdt = free;
                    lockedUsdt = locked;
                }

                totalEquityUsdt += totalValueUsdt;

                holdings.Add(new AssetHolding
                {
                    Asset = asset,
                    Free = free,
                    Locked = locked,
                    Total = total,
                    PriceUsdt = priceUsdt,
                    ValueUsdt = totalValueUsdt
                });
            }

            holdings = holdings.OrderByDescending(h => h.ValueUsdt).ToList();

            return new PortfolioSnapshot
            {
                Success = true,
                TotalEquityUsdt = totalEquityUsdt,
                FreeUsdt = freeUsdt,
                LockedUsdt = lockedUsdt,
                Holdings = holdings.Take(10).ToList()
            };
        }
        catch (Exception ex)
        {
            return new PortfolioSnapshot
            {
                Success = false,
                Error = $"Exception: {ex.Message}"
            };
        }
    }

    public async Task<decimal> GetStartOfDayEquityAsync(Repository.DbContext.CoinSoulDbContext db, CancellationToken ct)
    {
        var today = DateTime.UtcNow.Date;

        var snapshot = await db.EquitySnapshotEntity
            .Where(s => s.DayUtc == today)
            .OrderBy(s => s.AtUtc)
            .FirstOrDefaultAsync(ct);

        if (snapshot != null)
        {
            return snapshot.StartOfDayEquityUsdt;
        }

        var portfolio = await GetPortfolioAsync(ct);
        
        if (!portfolio.Success || portfolio.TotalEquityUsdt <= 0)
        {
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);
            if (settings != null && settings.EquityStartOfDayUsdt > 0)
            {
                return settings.EquityStartOfDayUsdt;
            }

            return 0;
        }

        var newSnapshot = new Entities.EquitySnapshotEntity
        {
            AtUtc = DateTime.UtcNow,
            DayUtc = today,
            TotalEquityUsdt = portfolio.TotalEquityUsdt,
            FreeUsdt = portfolio.FreeUsdt,
            LockedUsdt = portfolio.LockedUsdt,
            StartOfDayEquityUsdt = portfolio.TotalEquityUsdt,
            TopHoldings = JsonSerializer.Serialize(portfolio.Holdings.Take(5))
        };

        db.EquitySnapshotEntity.Add(newSnapshot);
        await db.SaveChangesAsync(ct);

        var botSettings = await db.BotSettings.FirstOrDefaultAsync(ct);
        if (botSettings != null)
        {
            botSettings.EquityStartOfDayUsdt = portfolio.TotalEquityUsdt;
            botSettings.LastStartUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);
        }

        return portfolio.TotalEquityUsdt;
    }

    public async Task SaveSnapshotAsync(
        Repository.DbContext.CoinSoulDbContext db,
        PortfolioSnapshot portfolio,
        decimal startOfDayEquity,
        CancellationToken ct)
    {
        if (!portfolio.Success)
            return;

        var snapshot = new Entities.EquitySnapshotEntity
        {
            AtUtc = DateTime.UtcNow,
            DayUtc = DateTime.UtcNow.Date,
            TotalEquityUsdt = portfolio.TotalEquityUsdt,
            FreeUsdt = portfolio.FreeUsdt,
            LockedUsdt = portfolio.LockedUsdt,
            StartOfDayEquityUsdt = startOfDayEquity,
            TopHoldings = JsonSerializer.Serialize(portfolio.Holdings.Take(5))
        };

        db.EquitySnapshotEntity.Add(snapshot);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Get current total equity in USDT
    /// </summary>
    public async Task<decimal> GetCurrentEquityAsync(CancellationToken ct = default)
    {
        try
        {
            var accountInfo = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!accountInfo.Success || accountInfo.Data == null)
            {
                return 0m;
            }

            var balances = accountInfo.Data.Balances.Where(b => b.Total > 0).ToList();
            
            if (!balances.Any())
            {
                return 0m;
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

            foreach (var balance in balances)
            {
                var asset = balance.Asset;
                var total = balance.Total;

                if (asset.Equals("USDT", StringComparison.OrdinalIgnoreCase))
                {
                    totalEquityUsdt += total;
                }
                else
                {
                    if (priceMap.TryGetValue(asset, out var priceUsdt))
                    {
                        totalEquityUsdt += total * priceUsdt;
                    }
                }
            }

            return totalEquityUsdt;
        }
        catch
        {
            return 0m;
        }
    }

    /// <summary>
    /// Get free and locked USDT balances
    /// </summary>
    public async Task<(decimal FreeUsdt, decimal LockedUsdt)> GetUsdtBalancesAsync(CancellationToken ct = default)
    {
        try
        {
            var accountInfo = await _client.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!accountInfo.Success || accountInfo.Data == null)
            {
                return (0, 0);
            }

            var usdtBalance = accountInfo.Data.Balances
                .FirstOrDefault(b => b.Asset.Equals("USDT", StringComparison.OrdinalIgnoreCase));

            if (usdtBalance == null)
            {
                return (0, 0);
            }

            return (usdtBalance.Available, usdtBalance.Locked);
        }
        catch
        {
            return (0, 0);
        }
    }
}

public sealed class PortfolioSnapshot
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public decimal TotalEquityUsdt { get; set; }
    public decimal FreeUsdt { get; set; }
    public decimal LockedUsdt { get; set; }
    public List<AssetHolding> Holdings { get; set; } = new();
}

public sealed class AssetHolding
{
    public string Asset { get; set; } = "";
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
    public decimal Total { get; set; }
    public decimal PriceUsdt { get; set; }
    public decimal ValueUsdt { get; set; }
}
