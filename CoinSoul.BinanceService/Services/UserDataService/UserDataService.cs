using AutoMapper;
using CoinSoul.BinanceService.Base;

namespace CoinSoul.BinanceService.Services.UserDataService
{
    public class UserDataService : BaseCoinSoulService, IUserDataService
    {
        public UserDataService(HttpClient httpClient, BinanceApplicationService applicationService, IMapper mapper)
            : base(httpClient, applicationService, mapper) { }

        //public Task IUserDataService.GetSpotWalletFunds()
        //{

        //}
    }
}
