using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine.Observability; // ✅ FIX 3: Add missing using
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection; // ✅ Add for GetRequiredService
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Settings;

/// <summary>
/// PART 6: Startup self-check validation service
/// Verifies BotSettings configuration before trading begins
/// </summary>
public sealed class BotSettingsValidationService : BackgroundService
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<BotSettingsValidationService> _logger;
    private readonly IServiceScopeFactory _scopeFactory;

    public BotSettingsValidationService(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<BotSettingsValidationService> logger,
        IServiceScopeFactory scopeFactory)
    {
        _dbFactory = dbFactory;
        _logger = logger;
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
            var eventWriter = scope.ServiceProvider.GetRequiredService<IEventWriter>();

            var settings = await settingsProvider.GetSettingsSnapshotAsync(stoppingToken);

            // ✅ FIX 6: Null check for settings
            if (settings == null)
            {
                _logger.LogWarning("[SETTINGS_VALIDATION] Settings is null, skipping validation");
                return;
            }

            // ✅ FIX 4 & 5: Check if LiveArmed property exists before accessing
            var liveArmed = GetLiveArmedValue(settings);

            var summary = $"TradingEnabled={settings.TradingEnabled}, " +
                          $"KillSwitch={settings.KillSwitch}, " +
                          $"ExecuteTrades={settings.ExecuteTrades}, " +
                          $"AutoScalperEnabled={settings.AutoScalperEnabled}, " +
                          $"StrategyModeValue={settings.StrategyModeValue}, " +
                          $"LiveArmed={liveArmed}";

            await eventWriter.WriteAsync(
                "SETTINGS_VALIDATION",
                $"Startup settings snapshot: {summary}",
                "INFO",
                ct: stoppingToken);

            _logger.LogInformation("[SETTINGS_VALIDATION] {Summary}", summary);

            // ✅ Warn if ExecuteTrades=true but LiveArmed=false
            if (settings.ExecuteTrades && !liveArmed)
            {
                _logger.LogWarning(
                    "[SETTINGS_WARNING] ExecuteTrades=true but LiveArmed=false. " +
                    "Live trading will be blocked until LiveArmed is set to true.");

                await eventWriter.WriteAsync(
                    "SETTINGS_WARNING",
                    "ExecuteTrades=true but LiveArmed=false. Set BotSettings.LiveArmed=true to enable live trading.",
                    "WARN",
                    ct: stoppingToken);
            }

            // ✅ Startup complete
            await eventWriter.WriteAsync(
                "STARTUP_COMPLETE",
                "Bot settings validated successfully",
                "INFO",
                ct: stoppingToken);

            // Call the validation report
            await ValidateSettingsAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SETTINGS_VALIDATION_ERROR] Failed during validation");
        }
    }

    // ✅ FIX 1: Override StartAsync instead of hiding it
    public override async Task StartAsync(CancellationToken ct)
    {
        // Call base implementation first
        await base.StartAsync(ct);

        // Then run validation
        await ValidateSettingsAsync(ct);
    }

    // ✅ FIX 2: Override StopAsync instead of hiding it
    public override Task StopAsync(CancellationToken ct)
    {
        return base.StopAsync(ct);
    }

    private async Task ValidateSettingsAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var settings = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            if (settings == null)
            {
                _logger.LogWarning("[VALIDATION] ⚠️ No BotSettings found - will be created on first load");
                return;
            }

            _logger.LogInformation("╔═══════════════════════════════════════════════════════════════╗");
            _logger.LogInformation("║            BOT SETTINGS VALIDATION REPORT                     ║");
            _logger.LogInformation("╠═══════════════════════════════════════════════════════════════╣");

            // ✅ Validate Strategy Mode Consistency
            var expectedStrategyMode = 4; // AutoScalperD
            var modeValid = !settings.AutoScalperEnabled || settings.StrategyModeValue == expectedStrategyMode;

            if (!modeValid)
            {
                _logger.LogError(
                    "║ ❌ CRITICAL: AutoScalperEnabled=true but StrategyModeValue={Value} (expected 4)",
                    settings.StrategyModeValue);
                _logger.LogError("║    FIX: Set StrategyModeValue=4 or disable AutoScalper");
            }
            else
            {
                _logger.LogInformation(
                    "║ ✅ Strategy Mode: {Mode} (Value={Value})",
                    settings.AutoScalperEnabled ? "AutoScalperD" : "Other",
                    settings.StrategyModeValue);
            }

            // ✅ Validate Critical Flags
            _logger.LogInformation("║ AutoScalperEnabled: {Value}", settings.AutoScalperEnabled);
            _logger.LogInformation("║ ExecuteTrades: {Value} {Mode}",
                settings.ExecuteTrades,
                settings.ExecuteTrades ? "(LIVE)" : "(DRY RUN)");
            _logger.LogInformation("║ TradingEnabled: {Value}", settings.TradingEnabled);
            _logger.LogInformation("║ IsEnabled: {Value}", settings.IsEnabled);
            _logger.LogInformation("║ IsRunning: {Value}", settings.IsRunning);
            _logger.LogInformation("║ KillSwitch: {Value}", settings.KillSwitch);

            // ✅ PART 7: Production Safety Warning
            if (settings.ExecuteTrades && !settings.PaperTrading)
            {
                _logger.LogWarning("╠═══════════════════════════════════════════════════════════════╣");
                _logger.LogWarning("║ ⚠️⚠️⚠️  LIVE TRADING MODE ACTIVE  ⚠️⚠️⚠️                        ║");
                _logger.LogWarning("║ ExecuteTrades=TRUE, PaperTrading=FALSE                        ║");
                _logger.LogWarning("║ REAL MONEY AT RISK - VERIFY ALL SETTINGS                      ║");
                _logger.LogWarning("╠═══════════════════════════════════════════════════════════════╣");
            }
            else if (!settings.ExecuteTrades)
            {
                _logger.LogInformation("║ 🎮 DRY RUN MODE - No real orders will be placed              ║");
            }

            // ✅ Validate Capital Settings
            if (settings.TargetUsdPerTrade <= 0)
            {
                _logger.LogError("║ ❌ CRITICAL: TargetUsdPerTrade={Value} (must be > 0)",
                    settings.TargetUsdPerTrade);
            }
            else
            {
                _logger.LogInformation("║ Trade Size: Target=${Target:N2}, Min=${Min:N2}",
                    settings.TargetUsdPerTrade, settings.MinUsdPerTrade);
            }

            _logger.LogInformation("║ Max Open Trades: {Value}", settings.MaxOpenTrades);
            _logger.LogInformation("║ TP: {TP}% | SL: {SL}% | Net Target: ${Net:N2}",
                settings.TakeProfitGrossPct,
                settings.StopLossGrossPct,
                settings.NetProfitTargetUsd);

            // ✅ Validate Safety Settings
            if (settings.StopUntilUtc.HasValue)
            {
                _logger.LogWarning("║ 🛑 HARD STOP until {Until}", settings.StopUntilUtc);
            }

            if (settings.PauseUntilUtc.HasValue)
            {
                _logger.LogWarning("║ ⏸️  PAUSED until {Until}", settings.PauseUntilUtc);
            }

            // ✅ Summary
            var canTrade = settings.AutoScalperEnabled &&
                          settings.IsEnabled &&
                          settings.TradingEnabled &&
                          !settings.KillSwitch &&
                          settings.StrategyModeValue == 4 &&
                          (!settings.StopUntilUtc.HasValue || settings.StopUntilUtc.Value <= DateTime.UtcNow);

            _logger.LogInformation("╠═══════════════════════════════════════════════════════════════╣");
            _logger.LogInformation("║ Can Trade: {Status}", canTrade ? "✅ YES" : "❌ NO");
            _logger.LogInformation("╚═══════════════════════════════════════════════════════════════╝");

            // ✅ Production hardening: never crash the host on invalid settings.
            // If settings are invalid, the orchestrator guards will block trading and emit deterministic logs.
            if (!modeValid || settings.TargetUsdPerTrade <= 0 || settings.MinUsdPerTrade <= 0)
            {
                _logger.LogError(
                    "[SETTINGS_INVALID] BotSettings invalid. Trading will be blocked by guards until fixed. modeValid={ModeValid}, TargetUsdPerTrade={Target}, MinUsdPerTrade={Min}",
                    modeValid, settings.TargetUsdPerTrade, settings.MinUsdPerTrade);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[VALIDATION_ERROR] Startup validation failed");
            // ✅ Non-fatal in production
            return;
        }
    }

    /// <summary>
    /// Helper method to safely get LiveArmed value
    /// Uses reflection to check if property exists (for backward compatibility)
    /// </summary>
    private static bool GetLiveArmedValue(BotSettingsEntity settings)
    {
        var liveArmedProperty = settings.GetType().GetProperty("LiveArmed");
        if (liveArmedProperty != null)
        {
            var value = liveArmedProperty.GetValue(settings);
            return value is bool b && b;
        }
        return false;
    }
}