using CoinSoul.Trading.Engine.Adaptive;
using Microsoft.AspNetCore.Mvc;

namespace CoinSoul.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AdaptiveStatsController : ControllerBase
{
    private readonly IScanScheduler _scheduler;

    public AdaptiveStatsController(IScanScheduler scheduler)
    {
        _scheduler = scheduler;
    }

    [HttpGet("stats")]
    public IActionResult GetStats()
    {
        var stats = _scheduler.GetStats();
        
        return Ok(new
        {
            RecentHitRatePercent = stats.RecentHitRatePercent,
            TotalScans = stats.TotalScans,
            AverageDelayMs = stats.AverageDelayMs,
            CategoryBreakdown = stats.CategoryCounts,
            TopCategories = stats.CategoryCounts
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
        });
    }
}