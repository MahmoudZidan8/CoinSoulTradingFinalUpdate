using Binance.Net.Enums;
using Binance.Net.Objects.Models;
using Binance.Net.Objects.Models.Futures.AlgoOrders;
using Binance.Net.Objects.Models.Spot;
using CoinSoul.BinanceService.AutoServices.SpotTradeService;
using CryptoExchange.Net.CommonObjects;
using CryptoExchange.Net.Objects;
using CoinSoul.Trading.Core; // Add this using directive at the top of the file

namespace CoinSoul.BinanceService.AutoServices.AccountDataService
{
    public interface IAutoAccountDataService
    {
        #region Orders
        public Task<IEnumerable<BinanceOrder>> GetOrdersAsync(string symbol, long? orderId = null, DateTime? startTime = null,
         DateTime? endTime = null, int? limit = null, int? receiveWindow = null, CancellationToken ct = default);

        public Task<BinancePlacedOrder> PlaceOrderAsync(string symbol, OrderSide side, SpotOrderType type, decimal? quantity = null, decimal? quoteQuantity = null,
          string? newClientOrderId = null, decimal? price = null, TimeInForce? timeInForce = null, decimal? stopPrice = null, decimal? icebergQty = null,
          OrderResponseType? orderResponseType = null, int? trailingDelta = null, int? strategyId = null, int? strategyType = null,
          SelfTradePreventionMode? selfTradePreventionMode = null, int? receiveWindow = null, CancellationToken ct = default);

        public Task<BinanceOrderBase> CancelOrderAsync(string symbol, long? orderId = null, string? origClientOrderId = null, string?
           newClientOrderId = null, CancelRestriction? cancelRestriction = null, long? receiveWindow = null, CancellationToken ct = default);

        public Task<BinanceOrder> GetOrderAsync(string symbol, long? orderId = null, string? origClientOrderId = null, long? receiveWindow = null,
            CancellationToken ct = default);

        public Task<IEnumerable<BinanceTrade>> GetUserTradesAsync(string symbol, long? orderId = null, DateTime? startTime = null, DateTime? endTime = null,
            int? limit = null, long? fromId = null, long? receiveWindow = null, CancellationToken ct = default);

        public Task<IEnumerable<BinanceOrder>> GetOpenOrdersAsync(string? symbol = null, int? receiveWindow = null, CancellationToken ct = default);

        public Task<BinanceAlgoOrders> GetClosedAlgoOrdersAsync(string? symbol = null, OrderSide? side = null, DateTime? startTime = null,
            DateTime? endTime = null, int? page = null, int? limit = null, long? receiveWindow = null, CancellationToken ct = default);

        public Task<IEnumerable<Balance>> GetBalancesAsync(string? accountId, CancellationToken ct);

        public Task<BinanceAccountInfo> GetAccountInfoAsync(long? receiveWindow = null, CancellationToken ct = default);

        public Task<IEnumerable<BinanceUserBalance>> GetBalancesAsync(string? asset = null, bool? needBtcValuation = null, int? receiveWindow = null,
            CancellationToken ct = default);

        #endregion
        Task<decimal> GetFreeAssetAsync(string asset, CancellationToken ct);

        Task<List<Trading.Core.SpotBalanceDto>> GetSpotBalancesAsync();
        Task<decimal> GetFreeUsdtAsync(CancellationToken ct);
        Task<decimal> GetLastPriceAsync(string symbol, CancellationToken ct);
        IAutoSpotTradeService GetSpotTradeService();

        // NEW: Aggregated trade history
        Task<List<AccountTradeRow>> GetAccountTradeHistoryAsync(
            DateTime fromUtc, 
            DateTime toUtc, 
            string? symbol = null, 
            int topSymbols = 80, 
            CancellationToken ct = default);
    }
}
