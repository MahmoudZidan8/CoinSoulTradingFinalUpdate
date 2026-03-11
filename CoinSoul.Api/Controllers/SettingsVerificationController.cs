#if DEBUG
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Api.Controllers;

/// <summary>
/// DEVELOPMENT ONLY - Settings persistence verification
/// </summary>
[ApiController]
[Route("admin/selftest")]
public class SettingsVerificationController : ControllerBase
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<SettingsVerificationController> _logger;

    public SettingsVerificationController(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<SettingsVerificationController> logger)
    {
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// GET /admin/selftest/settings
    /// Returns current BotSettings with all critical flags
    /// </summary>
    [HttpGet("settings")]
    public async Task<IActionResult> GetCurrentSettings(CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            
            var settings = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                return NotFound(new { error = "BotSettings not found" });
            }

            var result = new
            {
                testRunId = Guid.NewGuid().ToString(),
                timestamp = DateTime.UtcNow,
                databaseValues = new
                {
                    id = settings.Id,
                    
                    // ✅ CRITICAL FLAGS
                    autoScalperEnabled = settings.AutoScalperEnabled,
                    executeTrades = settings.ExecuteTrades,
                    killSwitch = settings.KillSwitch,
                    isEnabled = settings.IsEnabled,
                    tradingEnabled = settings.TradingEnabled,
                    isRunning = settings.IsRunning,
                    
                    // Trading parameters
                    targetUsdPerTrade = settings.TargetUsdPerTrade,
                    maxOpenTrades = settings.MaxOpenTrades,
                    takeProfitGrossPct = settings.TakeProfitGrossPct,
                    stopLossGrossPct = settings.StopLossGrossPct,
                    netProfitTargetUsd = settings.NetProfitTargetUsd,
                    
                    // Safety
                    pauseUntilUtc = settings.PauseUntilUtc,
                    stopUntilUtc = settings.StopUntilUtc,
                    maxAllowedEntrySlippagePct = settings.MaxAllowedEntrySlippagePct,
                    dustIgnoreUsdThreshold = settings.DustIgnoreUsdThreshold,
                    
                    // Reconciliation
                    reconcileIntervalSeconds = settings.ReconcileIntervalSeconds,
                    balanceRefreshCooldownMs = settings.BalanceRefreshCooldownMs,
                    
                    // Risk Guard
                    riskGuardPause30MinPct = settings.RiskGuardPause30MinPct,
                    riskGuardPause3HourPct = settings.RiskGuardPause3HourPct,
                    riskGuardStopUntilMidnightPct = settings.RiskGuardStopUntilMidnightPct
                },
                status = new
                {
                    canTrade = settings.AutoScalperEnabled && 
                              settings.IsEnabled && 
                              settings.TradingEnabled && 
                              !settings.KillSwitch &&
                              (!settings.StopUntilUtc.HasValue || settings.StopUntilUtc.Value <= DateTime.UtcNow),
                    
                    dryRunMode = !settings.ExecuteTrades,
                    
                    blockReasons = new List<string>()
                        .Concat(settings.AutoScalperEnabled ? Array.Empty<string>() : new[] { "AutoScalperEnabled=false" })
                        .Concat(settings.IsEnabled ? Array.Empty<string>() : new[] { "IsEnabled=false" })
                        .Concat(settings.TradingEnabled ? Array.Empty<string>() : new[] { "TradingEnabled=false" })
                        .Concat(settings.KillSwitch ? new[] { "KillSwitch=true" } : Array.Empty<string>())
                        .Concat(settings.StopUntilUtc.HasValue && settings.StopUntilUtc.Value > DateTime.UtcNow 
                            ? new[] { $"StopUntilUtc={settings.StopUntilUtc:yyyy-MM-dd HH:mm:ss}" } 
                            : Array.Empty<string>())
                        .ToList()
                }
            };

            _logger.LogInformation(
                "[SETTINGS_QUERY] AutoScalperEnabled={Auto}, CanTrade={CanTrade}",
                settings.AutoScalperEnabled,
                result.status.canTrade);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SETTINGS_QUERY_ERROR]");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// POST /admin/selftest/toggle-autoscalper
    /// Toggles AutoScalperEnabled for testing
    /// </summary>
    [HttpPost("toggle-autoscalper")]
    public async Task<IActionResult> ToggleAutoScalper(CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            
            var settings = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                return NotFound(new { error = "BotSettings not found" });
            }

            var before = settings.AutoScalperEnabled;
            settings.AutoScalperEnabled = !settings.AutoScalperEnabled;
            
            await db.SaveChangesAsync(ct);

            // Verify
            var after = await db.BotSettings
                .AsNoTracking()
                .Select(s => s.AutoScalperEnabled)
                .FirstAsync(ct);

            _logger.LogInformation(
                "[SETTINGS_TOGGLE] AutoScalperEnabled: {Before} -> {After} (DB shows: {Verified})",
                before, !before, after);

            return Ok(new
            {
                success = true,
                before = before,
                after = !before,
                verified = after,
                testPassed = after == !before
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SETTINGS_TOGGLE_ERROR]");
            return StatusCode(500, new { error = ex.Message });
        }
    }
}
#endif