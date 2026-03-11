using System;
using System.Threading;
using System.Threading.Tasks;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine.Adaptive;
using CoinSoul.Trading.Engine.Observability;
using CoinSoul.Trading.Engine.Observability.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

public sealed class TradingWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TradingWorker> _logger;
    private readonly bool _enableAdaptiveMode;

    public TradingWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<TradingWorker> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _enableAdaptiveMode = configuration.GetValue<bool>("EnableAdaptiveMode", false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WORKER_START] TradingWorker started | AdaptiveMode={Mode}",
            _enableAdaptiveMode);

        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var sp = scope.ServiceProvider;

                var orchestrator = sp.GetRequiredService<AutoScalperOrchestrator>();
                var settingsProvider = sp.GetRequiredService<ISettingsProvider>();
                var scanScheduler = sp.GetRequiredService<IScanScheduler>();
                var volatilityCalculator = sp.GetRequiredService<IVolatilityCalculator>();
                var queueManager = sp.GetRequiredService<SymbolQueueManager>();
                var clock = sp.GetRequiredService<IClock>();
                var eventWriter = sp.GetRequiredService<IEventWriter>();
                var guardLogger = sp.GetRequiredService<ILogger<TickEventGuard>>();

                // ✅ CREATE TICK EVENT GUARD - Guarantees 1+ event per tick
                await using var tickGuard = new TickEventGuard(eventWriter, guardLogger);

                // ✅ EVENT: TICK_START
                await tickGuard.MarkAsync("TICK_START", "Trading tick started");

                var settings = await settingsProvider.GetSettingsSnapshotAsync(stoppingToken);

                // ✅ EVENT: SETTINGS_SNAPSHOT
                await tickGuard.MarkAsync(
                    "SETTINGS_SNAPSHOT",
                    $"TradingEnabled={settings.TradingEnabled}, KillSwitch={settings.KillSwitch}, " +
                    $"ExecuteTrades={settings.ExecuteTrades}, AutoScalperEnabled={settings.AutoScalperEnabled}, " +
                    $"StrategyModeValue={settings.StrategyModeValue}, " +
                    $"TradingHours={settings.TradingStartTime?.ToString(@"hh\:mm")}-{settings.TradingEndTime?.ToString(@"hh\:mm")}",
                    "INFO");

                // ✅ CHECK: Trading disabled
                if (!settings.TradingEnabled || settings.KillSwitch)
                {
                    var reason = settings.KillSwitch ? "KillSwitch=true" : "TradingEnabled=false";
                    _logger.LogDebug("[WORKER_DISABLED] {Reason}", reason);

                    await tickGuard.MarkAsync("TICK_BLOCKED", $"Trading blocked: {reason}", "WARN");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    continue;
                }

                // ✅ CHECK: Trading hours
                if (!IsWithinTradingHours(settings, clock))
                {
                    _logger.LogDebug("[WORKER_HOURS] Outside trading hours");

                    await tickGuard.MarkAsync("TICK_OUTSIDE_HOURS",
                        $"Outside trading hours: {clock.UtcNow:HH:mm} UTC", "INFO");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    continue;
                }

                // ✅ EXECUTE TICK
                var tickResult = await orchestrator.ExecuteTickAsync(stoppingToken);

                // ✅ Record scan result if available
                if (tickResult.DiagnosticData.TryGetValue("ScannedCount", out var scannedObj) &&
                    tickResult.DiagnosticData.TryGetValue("PassedCount", out var passedObj))
                {
                    var scanned = Convert.ToInt32(scannedObj);
                    var passed = Convert.ToInt32(passedObj);

                    scanScheduler.RecordScanResult(scanned, passed);

                    if (scanned > 0)
                    {
                        await tickGuard.MarkAsync(
                            "SCAN_RESULT",
                            $"Scanned={scanned}, Passed={passed}",
                            passed > 0 ? "INFO" : "WARN");
                    }
                }

                // Calculate next delay
                var delay = await CalculateNextDelayAsync(
                    settings, tickResult, scanScheduler, volatilityCalculator, queueManager, stoppingToken);

                // ✅ EVENT: TICK_DONE
                await tickGuard.MarkAsync(
                    "TICK_DONE",
                    $"Tick completed | Stage={tickResult.Stage}, Success={tickResult.Success}, " +
                    $"BlockReason={tickResult.BlockReason ?? "N/A"}, NextDelayMs={delay}",
                    "INFO");

                _logger.LogDebug("[WORKER_TICK] Completed | CorrelationId={CorrelationId} | NextDelay={Delay}ms",
                    tickGuard.CorrelationId, delay);

                await Task.Delay(delay, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[WORKER_ERROR] Tick execution failed");

                // ✅ EVENT: TICK_EXCEPTION
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var eventWriter = scope.ServiceProvider.GetRequiredService<IEventWriter>();
                    await eventWriter.WriteAsync(
                        "TICK_EXCEPTION",
                        $"Tick execution failed: {ex.Message}",
                        "ERROR",
                        ct: CancellationToken.None);
                }
                catch
                {
                    // Ignore event write failures during exception handling
                }

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }

        _logger.LogInformation("[WORKER_STOP] TradingWorker stopped");
    }

    private async Task<int> CalculateNextDelayAsync(
        CoinSoul.Entities.BotSettingsEntity settings,
        TickResult tickResult,
        IScanScheduler scanScheduler,
        IVolatilityCalculator volatilityCalculator,
        SymbolQueueManager queueManager,
        CancellationToken ct)
    {
        if (!_enableAdaptiveMode)
            return settings.TickSeconds * 1000;

        try
        {
            var volatility = await volatilityCalculator.GetMarketVolatilityAsync(ct);
            var metrics = new CoinSoul.Trading.Engine.Adaptive.ScanMetrics(
                Regime: GetRegimeFromTickResult(tickResult),
                OpenPositionsCount: GetOpenPositionsCount(tickResult),
                MaxPositions: settings.MaxConcurrentPositions,
                VolatilityPct: volatility,
                CooldownCount: 0,
                TotalSymbols: 100);

            var decision = await scanScheduler.GetNextScanDelayAsync(metrics, ct);
            return decision.DelayMs;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ADAPTIVE_ERROR] Using default delay");
            return settings.TickSeconds * 1000;
        }
    }

    private bool IsWithinTradingHours(CoinSoul.Entities.BotSettingsEntity settings, IClock clock)
    {
        if (!settings.TradingStartTime.HasValue || !settings.TradingEndTime.HasValue)
            return true;

        var now = clock.UtcNow.TimeOfDay;
        var start = settings.TradingStartTime.Value;
        var end = settings.TradingEndTime.Value;

        return start <= end
            ? now >= start && now <= end
            : now >= start || now <= end;
    }

    private string GetRegimeFromTickResult(TickResult tickResult) =>
        tickResult.DiagnosticData.TryGetValue("Regime", out var regimeObj)
            ? regimeObj?.ToString() ?? "Unknown"
            : "Unknown";

    private int GetOpenPositionsCount(TickResult tickResult) =>
        tickResult.DiagnosticData.TryGetValue("OpenPositions", out var countObj)
            ? Convert.ToInt32(countObj)
            : 0;
}
