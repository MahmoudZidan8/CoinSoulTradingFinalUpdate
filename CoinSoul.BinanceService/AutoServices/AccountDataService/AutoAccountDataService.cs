using AutoMapper;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Futures.AlgoOrders;
using Binance.Net.Objects.Models.Spot;
using CoinSoul.BinanceService.API;
using CoinSoul.BinanceService.AutoServices.SpotTradeService;
using CoinSoul.BinanceService.Base;
using CoinSoul.Trading.Core;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.CommonObjects;

namespace CoinSoul.BinanceService.AutoServices.AccountDataService
{
    public class AutoAccountDataService : BaseCoinSoulService, IAutoAccountDataService
    {
        protected readonly IBinanceRestClient _binanceClient;
        private readonly IAutoSpotTradeService _spotTradeService;

        public AutoAccountDataService(
            HttpClient httpClient,
            BinanceApplicationService applicationService,
            IMapper mapper,
            IBinanceRestClient binanceClient,
            IAutoSpotTradeService spotTradeService)
            : base(httpClient, applicationService, mapper)
        {
            _binanceClient = binanceClient;
            _spotTradeService = spotTradeService;
        }

        #region Orders
        public async Task<decimal> GetFreeAssetAsync(string asset, CancellationToken ct)
        {
            // مثال: لو عندك Binance client داخلي اسمه _client
            var account = await _binanceClient.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!account.Success) return 0m;

            var bal = account.Data.Balances.FirstOrDefault(b =>
                b.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase));

            return bal?.Available ?? 0m;
        }

        public async Task<IEnumerable<BinanceOrder>> GetOrdersAsync(string symbol, long? orderId = null, DateTime? startTime = null,
            DateTime? endTime = null, int? limit = null, int? receiveWindow = null, CancellationToken ct = default)
        {
            var orderPlacedResult = await _binanceClient.SpotApi.Trading.GetOrdersAsync(symbol, orderId, startTime, endTime, limit, receiveWindow, ct);

            if (orderPlacedResult.Success)
            {
                return orderPlacedResult.Data;
            }
            else
            {
                throw new Exception(orderPlacedResult?.Error?.Message);
            }
        }

        public async Task<BinancePlacedOrder> PlaceOrderAsync(string symbol, OrderSide side, SpotOrderType type, decimal? quantity = null, decimal? quoteQuantity = null,
            string? newClientOrderId = null, decimal? price = null, TimeInForce? timeInForce = null, decimal? stopPrice = null, decimal? icebergQty = null,
            OrderResponseType? orderResponseType = null, int? trailingDelta = null, int? strategyId = null, int? strategyType = null,
            SelfTradePreventionMode? selfTradePreventionMode = null, int? receiveWindow = null, CancellationToken ct = default)
        {
            var orderPlacedResult = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(symbol, side, type, quantity, quoteQuantity, newClientOrderId, price,
                timeInForce, stopPrice, icebergQty, orderResponseType, trailingDelta, strategyId, strategyType, selfTradePreventionMode, receiveWindow, ct);

            if (orderPlacedResult.Success)
            {
                return orderPlacedResult.Data;
            }
            else
            {
                throw new Exception(orderPlacedResult?.Error?.Message);
            }
        }

        public async Task<BinanceOrderBase> CancelOrderAsync(string symbol, long? orderId = null, string? origClientOrderId = null, string?
            newClientOrderId = null, CancelRestriction? cancelRestriction = null, long? receiveWindow = null, CancellationToken ct = default)
        {
            var orderPlacedResult = await _binanceClient.SpotApi.Trading.CancelOrderAsync(symbol, orderId, origClientOrderId,
            newClientOrderId, cancelRestriction, receiveWindow, ct);

            if (orderPlacedResult.Success)
            {
                return orderPlacedResult.Data;
            }
            else
            {
                throw new Exception(orderPlacedResult?.Error?.Message);
            }
        }

        public async Task<BinanceOrder> GetOrderAsync(string symbol, long? orderId = null, string? origClientOrderId = null, long? receiveWindow = null,
            CancellationToken ct = default)
        {
            var orderGetResult = await _binanceClient.SpotApi.Trading.GetOrderAsync(symbol, orderId, origClientOrderId, receiveWindow,
            ct);

            if (orderGetResult.Success)
            {
                return orderGetResult.Data;
            }
            else
            {
                throw new Exception(orderGetResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<BinanceTrade>> GetUserTradesAsync(string symbol, long? orderId = null, DateTime? startTime = null, DateTime? endTime = null,
            int? limit = null, long? fromId = null, long? receiveWindow = null, CancellationToken ct = default)
        {
            var orderTradesGetResult = await _binanceClient.SpotApi.Trading.GetUserTradesAsync(symbol, orderId, startTime, endTime,
             limit, fromId, receiveWindow, ct);

            if (orderTradesGetResult.Success)
            {
                return orderTradesGetResult.Data;
            }
            else
            {
                throw new Exception(orderTradesGetResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<BinanceOrder>> GetOpenOrdersAsync(string? symbol = null, int? receiveWindow = null, CancellationToken ct = default)
        {
            var openOrdersResult = await _binanceClient.SpotApi.Trading.GetOpenOrdersAsync(symbol, receiveWindow, ct);

            if (openOrdersResult.Success)
            {
                return openOrdersResult.Data;
            }
            else
            {
                throw new Exception(openOrdersResult?.Error?.Message);
            }
        }

        public async Task<BinanceAlgoOrders> GetClosedAlgoOrdersAsync(string? symbol = null, OrderSide? side = null, DateTime? startTime = null,
            DateTime? endTime = null, int? page = null, int? limit = null, long? receiveWindow = null, CancellationToken ct = default)
        {
            var closedOrdersResult = await _binanceClient.SpotApi.Trading.GetClosedAlgoOrdersAsync(symbol, side, startTime,
             endTime, page, limit, receiveWindow, ct);

            if (closedOrdersResult.Success)
            {
                return closedOrdersResult.Data;
            }
            else
            {
                throw new Exception(closedOrdersResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<Balance>> GetBalancesAsync(string? accountId, CancellationToken ct)
        {
            var balancesResult = await _binanceClient.SpotApi.CommonSpotClient.GetBalancesAsync(accountId, ct);

            if (balancesResult.Success)
            {
                return balancesResult.Data;
            }
            else
            {
                throw new Exception(balancesResult?.Error?.Message);
            }
        }

        public async Task<BinanceAccountInfo> GetAccountInfoAsync(long? receiveWindow = null, CancellationToken ct = default)
        {
            var balancesResult = await _binanceClient.SpotApi.Account.GetAccountInfoAsync(receiveWindow, ct);

            if (balancesResult.Success)
            {
                return balancesResult.Data;
            }
            else
            {
                throw new Exception(balancesResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<BinanceUserBalance>> GetBalancesAsync(string? asset = null, bool? needBtcValuation = null, int? receiveWindow = null,
            CancellationToken ct = default)
        {
            var balancesResult = await _binanceClient.SpotApi.Account.GetBalancesAsync(asset, needBtcValuation, receiveWindow, ct);

            if (balancesResult.Success)
            {
                return balancesResult.Data;
            }
            else
            {
                throw new Exception(balancesResult?.Error?.Message);
            }
        }

        #endregion


        public async Task<List<SpotBalanceDto>> GetSpotBalancesAsync()
        {
            var account = await _binanceClient.SpotApi.Account.GetAccountInfoAsync();

            if (!account.Success)
                throw new Exception(account.Error?.Message ?? "Failed to load balances");

            return account.Data.Balances
                .Where(b => b.Available > 0 || b.Locked > 0)
                .Select(b => new SpotBalanceDto
                {
                    Asset = b.Asset,
                    Free = b.Available,
                    Locked = b.Locked
                })
                .ToList();
        }

        // ======================
        // Strategy D helpers
        // ======================


        public async Task<decimal> GetFreeUsdtAsync(CancellationToken ct)
        {
            var account = await _binanceClient.SpotApi.Account.GetAccountInfoAsync(ct: ct);
            if (!account.Success || account.Data is null)
                throw new Exception(account.Error?.Message ?? "Failed to load account info");

            var usdt = account.Data.Balances.FirstOrDefault(b => b.Asset == "USDT");
            return usdt?.Available ?? 0m;
        }

        public async Task<decimal> GetLastPriceAsync(string symbol, CancellationToken ct)
        {
            var p = await _binanceClient.SpotApi.ExchangeData.GetPriceAsync(symbol, ct);
            if (!p.Success || p.Data is null)
                throw new Exception(p.Error?.Message ?? "Failed to load price");

            return p.Data.Price;
        }

        public IAutoSpotTradeService GetSpotTradeService() => new AutoSpotTradeService(
            _httpClient, _applicationService, _mapper, _binanceClient,
            _binanceClient.SpotApi.CommonSpotClient);

        // ======================
        // NEW: Aggregated Trade History
        // ======================
        public async Task<List<AccountTradeRow>> GetAccountTradeHistoryAsync(
            DateTime fromUtc,
            DateTime toUtc,
            string? symbol = null,
            int topSymbols = 80,
            CancellationToken ct = default)
        {
            var allTrades = new List<AccountTradeRow>();
            var errorCount = 0;

            // If symbol is provided, get trades for that symbol only
            if (!string.IsNullOrWhiteSpace(symbol))
            {
                try
                {
                    var trades = await GetUserTradesAsync(symbol, null, fromUtc, toUtc, 1000, null, null, ct);
                    
                    foreach (var trade in trades)
                    {
                        allTrades.Add(MapToAccountTradeRow(trade, symbol));
                    }
                }
                catch (Exception)
                {
                    // Ignore failures for individual symbols
                    errorCount++;
                }
            }
            else
            {
                // Get top symbols by 24h quote volume
                var topSymbolsList = await GetTopUsdtSymbolsAsync(topSymbols, ct);

                // Fetch trades for each symbol
                foreach (var sym in topSymbolsList)
                {
                    try
                    {
                        var trades = await GetUserTradesAsync(sym, null, fromUtc, toUtc, 1000, null, null, ct);
                        
                        foreach (var trade in trades)
                        {
                            allTrades.Add(MapToAccountTradeRow(trade, sym));
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore failures for individual symbols
                        errorCount++;
                    }
                }
            }

            // Sort by trade time descending
            return allTrades.OrderByDescending(t => t.TradeTimeUtc).ToList();
        }

        private async Task<List<string>> GetTopUsdtSymbolsAsync(int limit, CancellationToken ct)
        {
            var tickersResult = await _binanceClient.SpotApi.ExchangeData.GetTickersAsync(ct);
            
            if (!tickersResult.Success || tickersResult.Data == null)
                throw new Exception(tickersResult?.Error?.Message ?? "Failed to load tickers");

            // Filter USDT pairs, exclude leveraged tokens
            return tickersResult.Data
                .Where(t => t.Symbol.EndsWith("USDT") && 
                           !t.Symbol.Contains("UP") && 
                           !t.Symbol.Contains("DOWN") &&
                           !t.Symbol.Contains("BULL") &&
                           !t.Symbol.Contains("BEAR"))
                .OrderByDescending(t => t.QuoteVolume)
                .Take(limit)
                .Select(t => t.Symbol)
                .ToList();
        }

        private AccountTradeRow MapToAccountTradeRow(BinanceTrade trade, string symbol)
        {
            return new AccountTradeRow
            {
                TradeTimeUtc = trade.Timestamp,
                Symbol = symbol,
                Side = trade.IsBuyer ? "BUY" : "SELL",
                Price = trade.Price,
                Quantity = trade.Quantity,
                QuoteQty = trade.QuoteQuantity,
                Commission = trade.Fee,
                CommissionAsset = trade.FeeAsset,
                OrderId = trade.OrderId,
                TradeId = trade.Id,
                IsBuyer = trade.IsBuyer,
                IsMaker = trade.IsMaker
            };
        }
    }
}
