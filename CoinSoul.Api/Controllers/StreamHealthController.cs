using CoinSoul.Trading.Engine.Streaming;
using Microsoft.AspNetCore.Mvc;

namespace CoinSoul.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StreamHealthController : ControllerBase
{
    private readonly IMarketStreamService _streamService;

    public StreamHealthController(IMarketStreamService streamService)
    {
        _streamService = streamService;
    }

    [HttpGet("status")]
    public IActionResult GetStatus()
    {
        var stats = _streamService.GetStats();
        
        return Ok(new
        {
            Healthy = _streamService.IsHealthy,
            Connected = stats.Connected,
            SubscribedSymbols = stats.SubscribedSymbolCount,
            BookTickerCount = stats.BookTickerCount,
            Ticker24hCount = stats.Ticker24hCount,
            LastDataReceived = stats.LastDataReceivedUtc,
            ReconnectCount = stats.ReconnectCount,
            UptimeSeconds = stats.Uptime.TotalSeconds
        });
    }
}