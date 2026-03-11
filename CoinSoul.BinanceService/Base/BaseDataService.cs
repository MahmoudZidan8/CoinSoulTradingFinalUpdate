using AutoMapper;
using Binance.Net.Clients;

namespace CoinSoul.BinanceService.Base
{
    public abstract class BaseDataService
    {
        protected readonly HttpClient _httpClient;
        protected readonly BinanceApplicationService _applicationService;
        protected readonly IMapper _mapper;

        public BaseDataService(HttpClient httpClient, BinanceApplicationService applicationService, IMapper mapper)
        {
            _httpClient = httpClient ??
                throw new ArgumentNullException(nameof(httpClient));

            _applicationService = applicationService ??
                throw new ArgumentNullException(nameof(applicationService));

            _mapper = mapper ??
                throw new ArgumentNullException(nameof(mapper));

            SetHttpClient();
        }

        protected virtual void SetHttpClient()
        {
            //Add Headers Here
        }

        public void SetUrl(string? url)
        {
            if (url is null) return;
            _httpClient.BaseAddress = new Uri(url);
        }
    }
}