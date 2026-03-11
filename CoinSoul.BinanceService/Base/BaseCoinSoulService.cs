using AutoMapper;
using Binance.Net.Clients;
using CoinSoul.BinanceService.API;

namespace CoinSoul.BinanceService.Base
{
    public class BaseCoinSoulService : BaseDataService
    {
        protected string _rquestBaseUrl = APINames.TestUrl;

        /// <summary>
        /// The default receive window for requests
        /// </summary>
        protected TimeSpan _receiveWindow { get; set; } = TimeSpan.FromSeconds(5);

        public BaseCoinSoulService(HttpClient httpClient, BinanceApplicationService applicationService, IMapper mapper)
           : base(httpClient, applicationService, mapper) { }
    }
}
