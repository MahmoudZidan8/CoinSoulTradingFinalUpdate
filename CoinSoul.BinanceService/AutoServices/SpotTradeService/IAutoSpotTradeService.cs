using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Objects.Models.Spot;
using Binance.Net.Objects.Models.Spot.Margin;

namespace CoinSoul.BinanceService.AutoServices.SpotTradeService
{
    public interface IAutoSpotTradeService
    {
        public Task<IEnumerable<BinanceTradeFee>> GetTradeFee(string? symbol = null, int? receiveWindow = null, CancellationToken ct = default);

        public Task<BinanceMarginAsset> GetMarginAssetAsync(string asset, CancellationToken ct = default);

        #region Test Connectivity

        public Task<long> TestConnectionAsync(CancellationToken ct = default);

        public Task<DateTime> GetServerTimeAsync(CancellationToken ct = default);

        /// <inheritdoc />
        public Task<BinanceSystemStatus> GetSystemStatusAsync(CancellationToken ct = default);
        #endregion

        #region 24hr Ticker Price Change Statistics

        public Task<IBinanceTick> GetTickerAsync(string symbol, CancellationToken ct = default);

        public Task<IEnumerable<IBinanceTick>> GetTickersAsync(IEnumerable<string> symbols, CancellationToken ct = default);

        #endregion

        #region Current Average Price

        /// <inheritdoc />
        public Task<BinanceAveragePrice> GetCurrentAvgPriceAsync(string symbol, CancellationToken ct = default);

        #endregion

        #region Exchange Information

        public Task<BinanceExchangeInfo> GetExchangeInfoAsync(CancellationToken ct = default);

        public Task<BinanceExchangeInfo> GetExchangeInfoAsync(string symbol, CancellationToken ct = default);

        public Task<BinanceExchangeInfo> GetExchangeInfoAsync(AccountType permission, CancellationToken ct = default);

        public Task<BinanceExchangeInfo> GetExchangeInfoAsync(AccountType[] permissions, CancellationToken ct = default);

        public Task<BinanceExchangeInfo> GetExchangeInfoAsync(IEnumerable<string> symbols, CancellationToken ct = default);

        #endregion

        #region Order Book
        public Task<BinanceOrderBook> GetOrderBookAsync(string symbol, int? limit = null, CancellationToken ct = default);
        #endregion

        #region Recent Trades List

        /// <inheritdoc />
        public Task<IEnumerable<IBinanceRecentTrade>> GetRecentTradesAsync(string symbol, int? limit = null, CancellationToken ct = default);

        #endregion

        #region Rolling window price change ticker

        public Task<IBinance24HPrice> GetRollingWindowTickerAsync(string symbol, TimeSpan? windowSize = null, CancellationToken ct = default);

        public Task<IEnumerable<IBinance24HPrice>> GetRollingWindowTickersAsync(IEnumerable<string> symbols, TimeSpan? windowSize = null, CancellationToken ct = default);

        #endregion

        #region Symbol Price Ticker
        public Task<BinancePrice> GetPriceAsync(string symbol, CancellationToken ct = default);

        public Task<IEnumerable<BinancePrice>> GetPricesAsync(IEnumerable<string> symbols, CancellationToken ct = default);
        #endregion

        #region Symbol Order Book Ticker
        public Task<BinanceBookPrice> GetBookPriceAsync(string symbol, CancellationToken ct = default);

        public Task<IEnumerable<BinanceBookPrice>> GetBookPricesAsync(IEnumerable<string> symbols, CancellationToken ct = default);
        #endregion

        Task<(bool Success, string? Error)> MarketSellAsync(
    string symbol,
    decimal quantity,
    CancellationToken ct = default);


    }
}
