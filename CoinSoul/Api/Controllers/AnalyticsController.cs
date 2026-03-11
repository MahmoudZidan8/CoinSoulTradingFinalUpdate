using CoinSoul.Trading.Engine.Analytics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CoinSoul.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AnalyticsController : ControllerBase
{
    private readonly AnalyticsService _analytics;
    private readonly ILogger<AnalyticsController> _logger;

    public AnalyticsController(AnalyticsService analytics, ILogger<AnalyticsController> logger)
    {
        _analytics = analytics;
        _logger = logger;
    }

    /// <summary>
    /// Gets comprehensive performance dashboard data
    /// </summary>
    [HttpGet("dashboard")]
    [ProducesResponseType(typeof(PerformanceDashboardDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetDashboard(CancellationToken ct)
    {
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var dashboard = await _analytics.GetDashboardAsync(nowUtc, ct);
            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving analytics dashboard");
            return StatusCode(500, new { error = "Failed to retrieve analytics" });
        }
    }

    /// <summary>
    /// Gets equity curve for specified period
    /// </summary>
    [HttpGet("equity")]
    [ProducesResponseType(typeof(List<EquityPointDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetEquityCurve([FromQuery] string period = "7d", CancellationToken ct = default)
    {
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var todayStart = new DateTimeOffset(nowUtc.Date, TimeSpan.Zero);
            
            var start = period.ToLowerInvariant() switch
            {
                "today" => todayStart,
                "7d" => todayStart.AddDays(-6),
                "30d" => todayStart.AddDays(-29),
                _ => todayStart.AddDays(-6)
            };

            var dashboard = await _analytics.GetDashboardAsync(nowUtc, ct);
            
            var curve = period.ToLowerInvariant() switch
            {
                "today" => dashboard.EquityCurveToday,
                "7d" => dashboard.EquityCurve7D,
                "30d" => dashboard.EquityCurve30D,
                _ => dashboard.EquityCurve7D
            };

            return Ok(curve);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving equity curve");
            return StatusCode(500, new { error = "Failed to retrieve equity curve" });
        }
    }

    /// <summary>
    /// Gets execution quality metrics
    /// </summary>
    [HttpGet("execution-quality")]
    [ProducesResponseType(typeof(ExecutionQualityDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetExecutionQuality(CancellationToken ct)
    {
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var dashboard = await _analytics.GetDashboardAsync(nowUtc, ct);
            return Ok(dashboard.ExecutionQuality);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving execution quality");
            return StatusCode(500, new { error = "Failed to retrieve execution quality" });
        }
    }

    /// <summary>
    /// Gets top rejection reasons
    /// </summary>
    [HttpGet("reject-reasons")]
    [ProducesResponseType(typeof(List<RejectReasonDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRejectReasons(CancellationToken ct)
    {
        try
        {
            var nowUtc = DateTimeOffset.UtcNow;
            var dashboard = await _analytics.GetDashboardAsync(nowUtc, ct);
            return Ok(dashboard.TopRejectReasonsToday);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving reject reasons");
            return StatusCode(500, new { error = "Failed to retrieve reject reasons" });
        }
    }
}