using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace CoinSole
{
    public class CoinSoleComponentBase : ComponentBase
    {
        [Inject]
        public required NavigationManager NavigationManager { get; set; }

        [Inject]
        public required IJSRuntime JSRuntime { get; set; }

        [Inject]
        public required HttpClient httpClient { get; set; }
    }
}
