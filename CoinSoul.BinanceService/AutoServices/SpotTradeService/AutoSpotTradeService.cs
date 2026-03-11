using AutoMapper;
using Binance.Net.Enums;
using Binance.Net.Interfaces;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models.Spot;
using Binance.Net.Objects.Models.Spot.Margin;
using CoinSoul.BinanceService.API;
using CoinSoul.BinanceService.Base;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Interfaces.CommonClients;
using System.Diagnostics;

namespace CoinSoul.BinanceService.AutoServices.SpotTradeService
{
    public class AutoSpotTradeService : BaseCoinSoulService, IAutoSpotTradeService
    {
        protected readonly IBinanceRestClient _binanceClient;
        protected readonly ISpotClient _spotClient;

        public AutoSpotTradeService(HttpClient httpClient, BinanceApplicationService applicationService, IMapper mapper, IBinanceRestClient binanceClient,
            ISpotClient spotClient)
          : base(httpClient, applicationService, mapper)
        {

            _binanceClient = binanceClient ??
         throw new ArgumentNullException(nameof(binanceClient));

            _spotClient = spotClient ??
         throw new ArgumentNullException(nameof(spotClient));

            //_binanceClient.SetApiCredentials(new ApiCredentials(APINames.ApiKey, APINames.SecretKey));
            if (!string.IsNullOrWhiteSpace(APINames.ApiKey) &&
    !string.IsNullOrWhiteSpace(APINames.SecretKey))
            {
                _binanceClient.SetApiCredentials(
                    new ApiCredentials(APINames.ApiKey, APINames.SecretKey));
            }

        }

        public async Task<BinanceMarginAsset> GetMarginAssetAsync(string asset, CancellationToken ct = default)
        {
            var marginAssetResul = await _binanceClient.SpotApi.ExchangeData.GetMarginAssetAsync(asset, ct);

            if (marginAssetResul.Success)
            {
                return marginAssetResul.Data;
            }
            else
            {
                throw new Exception(marginAssetResul?.Error?.Message);
            }
        }

        #region Test Connectivity
        public async Task<long> TestConnectionAsync(CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();
            var connectionResult = await _binanceClient.SpotApi.ExchangeData.PingAsync(ct);
            sw.Stop();

            if (connectionResult.Success)
            {
                return connectionResult ? sw.ElapsedMilliseconds : default;
            }
            else
            {
                throw new Exception(connectionResult?.Error?.Message);
            }
        }

        public async Task<DateTime> GetServerTimeAsync(CancellationToken ct = default)
        {
            var serverTimeResult = await _binanceClient.SpotApi.ExchangeData.GetServerTimeAsync(ct);

            if (serverTimeResult.Success)
            {
                return serverTimeResult.Data;
            }
            else
            {
                throw new Exception(serverTimeResult?.Error?.Message);
            }
        }

        /// <inheritdoc />
        public async Task<BinanceSystemStatus> GetSystemStatusAsync(CancellationToken ct = default)
        {
            var systemStatusResult = await _binanceClient.SpotApi.ExchangeData.GetSystemStatusAsync(ct);

            if (systemStatusResult.Success)
            {
                return systemStatusResult.Data;
            }
            else
            {
                throw new Exception(systemStatusResult?.Error?.Message);
            }
        }
        #endregion

        #region Current Average Price

        /// <inheritdoc />
        public async Task<BinanceAveragePrice> GetCurrentAvgPriceAsync(string symbol, CancellationToken ct = default)
        {
            var currentPriceAvgResult = await _binanceClient.SpotApi.ExchangeData.GetCurrentAvgPriceAsync(symbol, ct);

            if (currentPriceAvgResult.Success)
            {
                return currentPriceAvgResult.Data;
            }
            else
            {
                throw new Exception(currentPriceAvgResult?.Error?.Message);
            }
        }

        #endregion

        #region 24hr Ticker Price Change Statistics

        public async Task<IBinanceTick> GetTickerAsync(string symbol, CancellationToken ct = default)
        {
            var tickerResult = await _binanceClient.SpotApi.ExchangeData.GetTickerAsync(symbol, ct);

            if (tickerResult.Success)
            {
                return tickerResult.Data;
            }
            else
            {
                throw new Exception(tickerResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<IBinanceTick>> GetTickersAsync(IEnumerable<string> symbols, CancellationToken ct = default)
        {
            var tickerResult = await _binanceClient.SpotApi.ExchangeData.GetTickersAsync(symbols, ct);

            if (tickerResult.Success)
            {
                return tickerResult.Data;
            }
            else
            {
                throw new Exception(tickerResult?.Error?.Message);
            }
        }

        #endregion

        #region Rolling window price change ticker
        public async Task<IBinance24HPrice> GetRollingWindowTickerAsync(string symbol, TimeSpan? windowSize = null, CancellationToken ct = default)
        {
            var rollingWindowTickerResult = await _binanceClient.SpotApi.ExchangeData.GetRollingWindowTickerAsync(symbol, windowSize, ct);

            if (rollingWindowTickerResult.Success)
            {
                return rollingWindowTickerResult.Data;
            }
            else
            {
                throw new Exception(rollingWindowTickerResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<IBinance24HPrice>> GetRollingWindowTickersAsync(IEnumerable<string> symbols, TimeSpan? windowSize = null, CancellationToken ct = default)
        {
            var rollingWindowTickerResult = await _binanceClient.SpotApi.ExchangeData.GetRollingWindowTickersAsync(symbols, windowSize, ct);

            if (rollingWindowTickerResult.Success)
            {
                return rollingWindowTickerResult.Data;
            }
            else
            {
                throw new Exception(rollingWindowTickerResult?.Error?.Message);
            }
        }
        #endregion

        #region Symbol Price Ticker

        public async Task<BinancePrice> GetPriceAsync(string symbol, CancellationToken ct = default)
        {
            var priceResult = await _binanceClient.SpotApi.ExchangeData.GetPriceAsync(symbol, ct);

            if (priceResult.Success)
            {
                return priceResult.Data;
            }
            else
            {
                throw new Exception(priceResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<BinancePrice>> GetPricesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
        {
            var priceResult = await _binanceClient.SpotApi.ExchangeData.GetPricesAsync(symbols, ct);

            if (priceResult.Success)
            {
                return priceResult.Data;
            }
            else
            {
                throw new Exception(priceResult?.Error?.Message);
            }
        }

        #endregion

        #region Symbol Order Book Ticker

        public async Task<BinanceBookPrice> GetBookPriceAsync(string symbol, CancellationToken ct = default)
        {
            var bookPriceResult = await _binanceClient.SpotApi.ExchangeData.GetBookPriceAsync(symbol, ct);

            if (bookPriceResult.Success)
            {
                return bookPriceResult.Data;
            }
            else
            {
                throw new Exception(bookPriceResult?.Error?.Message);
            }
        }

        public async Task<IEnumerable<BinanceBookPrice>> GetBookPricesAsync(IEnumerable<string> symbols, CancellationToken ct = default)
        {
            var bookPriceResult = await _binanceClient.SpotApi.ExchangeData.GetBookPricesAsync(symbols, ct);

            if (bookPriceResult.Success)
            {
                return bookPriceResult.Data;
            }
            else
            {
                throw new Exception(bookPriceResult?.Error?.Message);
            }
        }

        #endregion

        #region Exchange Information

        /// <inheritdoc />
        public async Task<BinanceExchangeInfo> GetExchangeInfoAsync(CancellationToken ct = default)
        {
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync(ct);

            if (exchangeInfoResult.Success)
            {
                return exchangeInfoResult.Data;
            }
            else
            {
                throw new Exception(exchangeInfoResult?.Error?.Message);
            }
        }

        public async Task<BinanceExchangeInfo> GetExchangeInfoAsync(string symbol, CancellationToken ct = default)
        {
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync(ct);

            if (exchangeInfoResult.Success)
            {
                return exchangeInfoResult.Data;
            }
            else
            {
                throw new Exception(exchangeInfoResult?.Error?.Message);
            }
        }

        public async Task<BinanceExchangeInfo> GetExchangeInfoAsync(AccountType permission, CancellationToken ct = default)
        {
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync(permission, ct);

            if (exchangeInfoResult.Success)
            {
                return exchangeInfoResult.Data;
            }
            else
            {
                throw new Exception(exchangeInfoResult?.Error?.Message);
            }
        }

        public async Task<BinanceExchangeInfo> GetExchangeInfoAsync(AccountType[] permissions, CancellationToken ct = default)
        {
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync(permissions, ct);

            if (exchangeInfoResult.Success)
            {
                return exchangeInfoResult.Data;
            }
            else
            {
                throw new Exception(exchangeInfoResult?.Error?.Message);
            }
        }

        public async Task<BinanceExchangeInfo> GetExchangeInfoAsync(IEnumerable<string> symbols, CancellationToken ct = default)
        {
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetExchangeInfoAsync(symbols, ct);

            if (exchangeInfoResult.Success)
            {
                return exchangeInfoResult.Data;
            }
            else
            {
                throw new Exception(exchangeInfoResult?.Error?.Message);
            }
        }

        #endregion

        #region GetTradeFee
        public async Task<IEnumerable<BinanceTradeFee>> GetTradeFee(string? symbol = null, int? receiveWindow = null, CancellationToken ct = default)
        {
            var tradingFeeResult = await _binanceClient.SpotApi.ExchangeData.GetTradeFeeAsync(symbol, receiveWindow, ct);

            if (tradingFeeResult.Success)
            {
                return tradingFeeResult.Data;
            }
            else
            {
                throw new Exception(tradingFeeResult?.Error?.Message);
            }
        }
        #endregion

        #region Order Book

        /// <inheritdoc />
        public async Task<BinanceOrderBook> GetOrderBookAsync(string symbol, int? limit = null, CancellationToken ct = default)
        {
            var exchangeInfoResult = await _binanceClient.SpotApi.ExchangeData.GetOrderBookAsync(symbol, limit, ct);

            if (exchangeInfoResult.Success)
            {
                return exchangeInfoResult.Data;
            }
            else
            {
                throw new Exception(exchangeInfoResult?.Error?.Message);
            }
        }

        #endregion

        #region Recent Trades List

        /// <inheritdoc />
        public async Task<IEnumerable<IBinanceRecentTrade>> GetRecentTradesAsync(string symbol, int? limit = null, CancellationToken ct = default)
        {
            var recentTradesResult = await _binanceClient.SpotApi.ExchangeData.GetRecentTradesAsync(symbol, limit, ct);

            if (recentTradesResult.Success)
            {
                return recentTradesResult.Data;
            }
            else
            {
                throw new Exception(recentTradesResult?.Error?.Message);
            }
        }

        #endregion

        #region Old Trade Lookup

        /// <inheritdoc />
        public async Task<IEnumerable<IBinanceRecentTrade>> GetTradeHistoryAsync(string symbol, int? limit = null, long? fromId = null, CancellationToken ct = default)
        {
            var tradeHistoryResult = await _binanceClient.SpotApi.ExchangeData.GetTradeHistoryAsync(symbol, limit, fromId, ct);

            if (tradeHistoryResult.Success)
            {
                return tradeHistoryResult.Data;
            }
            else
            {
                throw new Exception(tradeHistoryResult?.Error?.Message);
            }
        }

        #endregion
        public async Task<(bool Success, string? Error)> MarketSellAsync(
    string symbol,
    decimal quantity,
    CancellationToken ct = default)
        {
            var res = await _binanceClient.SpotApi.Trading.PlaceOrderAsync(
                symbol: symbol,
                side: OrderSide.Sell,
                type: SpotOrderType.Market,
                quantity: quantity,
                ct: ct);

            if (!res.Success)
                return (false, res.Error?.Message);

            return (true, null);
        }

    }
}
