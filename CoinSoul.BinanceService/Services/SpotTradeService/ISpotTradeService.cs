using CoinSoul.Models.BinanceServiceModels.Spot;

namespace CoinSoul.BinanceService.Services.SpotTradeService
{
    public interface ISpotTradeService
    {
        public Task<BinanceTradeFeeModel?> GetTradeFee(string? symbol = null, int? receiveWindow = null, CancellationToken ct = default);

        public Task<ExchangeInfoModel?> GetPriceTicker(List<string> coinSymbols);

        public Task<Daily24HoursChangeModel?> Get24HoursChange(List<string> coinSymbols, string type);

        public Task<string?> GetBestPriceTicker(List<string> coinSymbols);

        public Task<string?> GetCurrentAveragePrice(string symbol);

        public Task<ExchangeInfoModel?> GetExchangeInformation(List<string> coinSymbols, List<string> permission);

        public Task<string?> GetDayTracker(List<string> coinSymbols, string timezone, string type);

        public Task<string?> GetOrderBook(string symbol, int limit);

        public Task<string?> GetPriceTracker(List<string> coinSymbols);

        public Task<bool?> TestConnection();
    }
}
