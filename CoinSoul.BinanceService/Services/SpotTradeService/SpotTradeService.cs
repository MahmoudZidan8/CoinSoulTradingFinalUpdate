using AutoMapper;
using CoinSoul.BinanceService.Base;
using CoinSoul.Models.BinanceServiceModels.Spot;
using Newtonsoft.Json;
using System.Globalization;

namespace CoinSoul.BinanceService.Services.SpotTradeService
{
    public class SpotTradeService : BaseCoinSoulService, ISpotTradeService
    {
        public SpotTradeService(HttpClient httpClient, BinanceApplicationService applicationService, IMapper mapper)
            : base(httpClient, applicationService, mapper) { }

        /// <summary>
        /// Get 24 hours change
        /// </summary>
        /// <param name="coinSymbols">Coin (ex. BTCUSDT)</param>
        /// <param name="type"> type (Full Or Mini)</param>
        /// <returns>True if </returns>
        public async Task<Daily24HoursChangeModel?> Get24HoursChange(List<string> coinSymbols, string type = "")
        {
            var upType = type.ToUpper();

            var urlRequest = $"{_rquestBaseUrl}/v3/ticker/24hr";

            var symbolsRequest = GetSymbols(coinSymbols);

            urlRequest += string.IsNullOrEmpty(symbolsRequest) ? string.Empty : symbolsRequest;

            if (string.IsNullOrEmpty(symbolsRequest))
            {
                urlRequest += $"?type={(string.IsNullOrEmpty(upType) ? string.Empty : upType)}";
            }
            else
            {
                urlRequest += $"&type={(string.IsNullOrEmpty(upType) ? string.Empty : upType)}";
            }

            HttpResponseMessage httpResponse = await _httpClient.GetAsync(urlRequest);

            var x = JsonConvert.DeserializeObject<Daily24HoursChangeModel>(
                await httpResponse.Content.ReadAsStringAsync());

            return httpResponse.IsSuccessStatusCode ? JsonConvert.DeserializeObject<Daily24HoursChangeModel>(
                await httpResponse.Content.ReadAsStringAsync()) : null;
        }

        public async Task<BinanceTradeFeeModel?> GetTradeFee(string? symbol = "BTCUSDT", int? receiveWindow = null, CancellationToken ct = default)
        {
            var urlRequest = $"{_rquestBaseUrl}/v3/asset/tradeFee";

            string receiveWindowString = receiveWindow?.ToString(CultureInfo.InvariantCulture) ?? _receiveWindow.TotalMilliseconds.ToString(CultureInfo.InvariantCulture);

            urlRequest += $"?symbol=\"{symbol}\"&recvWindow={receiveWindowString}";

            HttpResponseMessage httpResponse = await _httpClient.GetAsync(urlRequest);

            var x = JsonConvert.DeserializeObject<BinanceTradeFeeModel>(
                await httpResponse.Content.ReadAsStringAsync());

            return httpResponse.IsSuccessStatusCode ? JsonConvert.DeserializeObject<BinanceTradeFeeModel>(
                await httpResponse.Content.ReadAsStringAsync()) : null;
        }

        public async Task<string?> GetBestPriceTicker(List<string> coinSymbols)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Gets the coin average price
        /// </summary>
        /// <param name="symbol">Coin (ex. BTCUSDT)</param>
        /// <returns>Average price of the coin</returns>
        public async Task<string?> GetCurrentAveragePrice(string symbol)
        {
            throw new NotImplementedException();
        }

        public async Task<string?> GetDayTracker(List<string> coinSymbols, string timezone, string type)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Geg Exchange Information
        /// </summary>
        /// <param name="coinSymbols">Coin to get Exchange information (ex. BTCUSDT)</param>
        /// <param name="permissions">the permission (ex. Spot)</param>
        /// <returns>List of Exchange Information</returns>
        public async Task<ExchangeInfoModel?> GetExchangeInformation(List<string> coinSymbols, List<string> permissions)
        {
            var urlRequest = $"{_rquestBaseUrl}/v3/exchangeInfo";

            var symbolsRequest = GetSymbols(coinSymbols);
            var permissionsRequest = GetPermissions(string.IsNullOrEmpty(symbolsRequest), coinSymbols);

            urlRequest += string.IsNullOrEmpty(symbolsRequest) ? string.Empty : symbolsRequest;

            urlRequest += string.IsNullOrEmpty(permissionsRequest) ? string.Empty : permissionsRequest;

            HttpResponseMessage httpResponse = await _httpClient.GetAsync(urlRequest);

            var x = JsonConvert.DeserializeObject<ExchangeInfoModel>(
                await httpResponse.Content.ReadAsStringAsync());

            return httpResponse.IsSuccessStatusCode ? JsonConvert.DeserializeObject<ExchangeInfoModel>(
                await httpResponse.Content.ReadAsStringAsync()) : null;
        }

        /// <summary>
        /// Get Coin Order Book
        /// </summary>
        /// <param name="symbol">Coin to get orders information (ex. BTCUSDT)</param>
        /// <param name="limit">Limit of orders (ex 50)</param>
        /// <returns>Orders in the order book</returns>
        public async Task<string?> GetOrderBook(string symbol, int limit)
        {
            throw new NotImplementedException();
        }

        public async Task<string?> GetPriceTracker(List<string> coinSymbols)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Testing the Binance Network is fine
        /// </summary>
        /// <returns>True if connection is fine</returns>
        public async Task<bool?> TestConnection()
        {
            HttpResponseMessage httpResponse =
                  await _httpClient.GetAsync($"{_rquestBaseUrl}/v3/ping");

            //Add Resource When available
            return httpResponse.IsSuccessStatusCode ? true : false;
        }


        public async Task<ExchangeInfoModel?> GetPriceTicker(List<string> coinSymbols)
        {
            var urlRequest = $"{_rquestBaseUrl}/v3/exchangeInfo";

            var symbolsRequest = GetSymbols(coinSymbols);

            urlRequest += string.IsNullOrEmpty(symbolsRequest) ? string.Empty : symbolsRequest;

            HttpResponseMessage httpResponse = await _httpClient.GetAsync(urlRequest);

            var x = JsonConvert.DeserializeObject<object>(
                await httpResponse.Content.ReadAsStringAsync());

            return httpResponse.IsSuccessStatusCode ? JsonConvert.DeserializeObject<ExchangeInfoModel>(
                await httpResponse.Content.ReadAsStringAsync()) : null;
        }

        private string GetSymbols(List<string> coinSymbols)
        {
            var symbols = string.Empty;
            if (coinSymbols?.Count == 1)
            {
                var upperSymbol = coinSymbols[0].ToUpper();
                symbols += $"?symbol={upperSymbol}";
            }
            else if (coinSymbols?.Count >= 1)
            {
                var upperSymbol = string.Join("\",\"", coinSymbols).ToUpper();
                var symbolsRequest = upperSymbol;
                symbolsRequest += "\"]";

                symbols += $"?symbols={symbolsRequest}";
            }

            return symbols;
        }

        private string GetPermissions(bool IsRequestContainesSymbol, List<string> permissions)
        {
            var permissionsRequest = string.Empty;

            if (IsRequestContainesSymbol)
            {
                permissionsRequest = "&permissions=";
            }
            else
            {
                permissionsRequest = "?permissions=";
            }

            if (permissions?.Count == 1)
            {
                permissionsRequest += permissions[0];
            }
            else if (permissions?.Count >= 1)
            {
                permissionsRequest += String.Join("\",\"", permissions);
                permissionsRequest += "\"]";
            }

            return permissionsRequest.ToUpper();
        }
    }
}
