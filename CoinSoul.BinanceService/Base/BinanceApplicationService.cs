namespace CoinSoul.BinanceService.Base
{
    public class BinanceApplicationService(IHttpClientFactory clientFactory)
    {
        private readonly IHttpClientFactory _clientFactory = clientFactory ??
                throw new ArgumentNullException(nameof(clientFactory));

        public async Task Setup(int userId, string serviceUrl)
        {
            HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, serviceUrl + "/api/UserManager/" + userId);
            request.Headers.Add("Accept", "application/json");

            HttpClient? client = _clientFactory.CreateClient();
            client.BaseAddress = new Uri(serviceUrl);
        }
    }
}
