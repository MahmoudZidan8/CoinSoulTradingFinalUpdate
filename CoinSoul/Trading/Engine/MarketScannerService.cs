using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Background service that continuously scans market for opportunities
/// and fills the SymbolQueueManager with prioritized candidates
/// </summary>
public sealed class MarketScannerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<MarketScannerService> _logger;
    private readonly int _scanIntervalSeconds;

    public MarketScannerService(
        IServiceScopeFactory scopeFactory,
        ILogger<MarketScannerService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _scanIntervalSeconds = configuration.GetValue<int>("MarketScanIntervalSeconds", 5);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogCritical("[SCANNER_BOOT] MarketScannerService started with interval={Interval}s",
            _scanIntervalSeconds);

        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); // Initial delay

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // ✅ CREATE SCOPE for each scan
                using var scope = _scopeFactory.CreateScope();
                var serviceProvider = scope.ServiceProvider;

                // ✅ RESOLVE SCOPED SERVICES
                var detector = serviceProvider.GetRequiredService<OpportunityDetector>();
                var queueManager = serviceProvider.GetRequiredService<SymbolQueueManager>();
                var settingsProvider = serviceProvider.GetRequiredService<CoinSoul.Trading.Application.ISettingsProvider>();

                var settings = await settingsProvider.GetSettingsSnapshotAsync(stoppingToken);

                if (!settings.TradingEnabled)
                {
                    _logger.LogDebug("[SCANNER_DISABLED] Trading disabled, skipping scan");
                    await Task.Delay(TimeSpan.FromSeconds(_scanIntervalSeconds * 2), stoppingToken);
                    continue;
                }

                var queueCount = queueManager.Snapshot().Count;

                // ✅ Always refresh queue on every scan interval to keep opportunities warm
                _logger.LogCritical("[SCANNER_START] Queue={Count}/{Max}, refreshing ranked candidates...",
                    queueCount, queueManager.MaxQueueSize);

                // Convert entity to BotSettings
                var botSettings = ConvertToBotSettings(settings);

                var scanResult = await detector.ScanTopAsync(
                    botSettings,
                    takeTop: queueManager.MaxQueueSize,
                    minScanSeconds: 0,
                    stoppingToken);

                var candidates = scanResult.Candidates;
                var diagnostics = scanResult.Diagnostics;

                // ✅ DIAGNOSTIC: Log how many symbols retrieved from detector
                _logger.LogCritical(
                    "[SCANNER_DETECTOR_RESULT] Retrieved {Count} candidates from OpportunityDetector",
                    candidates.Count);

                if (candidates.Count > 0)
                {
                    var queuedSymbols = candidates.Select(c =>
                        new SymbolQueueManager.QueuedSymbol(c.Symbol, c.Score, c.Reason)).ToList();

                    // ✅ DIAGNOSTIC: Log before enqueue
                    _logger.LogCritical("[SCANNER_ENQUEUE_START] Enqueueing {Count} symbols to SymbolQueueManager",
                        queuedSymbols.Count);

                    // This now resolves to the SINGLETON instance
                    queueManager.ReplaceQueue(queuedSymbols, correlationId: $"scan-{DateTime.UtcNow:HHmmss}");
                    var afterEnqueueCount = queueManager.Snapshot().Count;
                    var actualEnqueued = afterEnqueueCount;

                    // ✅ DIAGNOSTIC: Log comprehensive scanner stats
                    _logger.LogCritical(
                        "[SCANNER_STATS] Total={Total}, PrefilterPassed={Pre}, DeepAnalyzed={Deep}, " +
                        "FinalPassed={Final}, ActualEnqueued={Enqueued}/{Attempted} | " +
                        "PrefilterMs={PreMs:F0}, DeepMs={DeepMs:F0}, TotalMs={TotalMs:F0}",
                        diagnostics.TotalScanned,
                        diagnostics.PrefilterPassed,
                        diagnostics.DeepAnalyzed,
                        diagnostics.FinalPassed,
                        actualEnqueued,
                        candidates.Count,
                        diagnostics.PrefilterDuration.TotalMilliseconds,
                        diagnostics.DeepAnalyzeDuration.TotalMilliseconds,
                        diagnostics.TotalDuration.TotalMilliseconds);

                    // ✅ DIAGNOSTIC: Log top rejection reasons
                    var topRejects = diagnostics.RejectionCounts
                        .Where(kvp => kvp.Value > 0 && kvp.Key != "TOTAL_SCANNED")
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => $"{kvp.Key}={kvp.Value}")
                        .ToList();

                    if (topRejects.Count > 0)
                    {
                        _logger.LogWarning("[SCANNER_REJECTIONS] TopReasons=[{Reasons}]",
                            string.Join(", ", topRejects));
                    }

                    _logger.LogInformation(
                        "[SCANNER_SUCCESS] Added {Enqueued}/{Attempted} candidates to queue | QueueSize={QueueSize}/{MaxQueueSize}",
                        actualEnqueued,
                        candidates.Count,
                        afterEnqueueCount,
                        queueManager.MaxQueueSize);
                }
                else
                {
                    // ✅ DIAGNOSTIC: Log why no candidates found
                    var topRejects = diagnostics.RejectionCounts
                        .Where(kvp => kvp.Value > 0)
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => $"{kvp.Key}={kvp.Value}")
                        .ToList();

                    _logger.LogCritical(
                        "[SCANNER_EMPTY] No candidates found | Scanned={Total}, PrefilterPassed={Pre}, " +
                        "TopRejects=[{Reasons}]",
                        diagnostics.TotalScanned,
                        diagnostics.PrefilterPassed,
                        string.Join(", ", topRejects));
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SCANNER_ERROR] Market scan failed");
            }

            await Task.Delay(TimeSpan.FromSeconds(_scanIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("[SCANNER_STOP] MarketScannerService stopped");
    }

    private static CoinSoul.Trading.Core.BotSettings ConvertToBotSettings(CoinSoul.Entities.BotSettingsEntity entity)
    {
        var tradeMode = Enum.TryParse<CoinSoul.Trading.Core.TradeMode>(entity.TradeMode, out var parsedTradeMode)
            ? parsedTradeMode
            : CoinSoul.Trading.Core.TradeMode.Spot; 

        var strategyAMode = Enum.TryParse<CoinSoul.Trading.Core.StrategyAMode>(entity.StrategyModeValue.ToString(), out var parsedStrategyAMode)
            ? parsedStrategyAMode
            : CoinSoul.Trading.Core.StrategyAMode.Conservative;

        // Fix: Use StrategyModeValue for StrategyMode conversion
        var strategyMode = Enum.IsDefined(typeof(CoinSoul.Trading.Core.StrategyMode), entity.StrategyModeValue)
            ? (CoinSoul.Trading.Core.StrategyMode)entity.StrategyModeValue
            : CoinSoul.Trading.Core.StrategyMode.None;

        return new CoinSoul.Trading.Core.BotSettings
        {
            TradeMode = tradeMode,
            StrategyAMode = strategyAMode,
            StrategyMode = strategyMode,
            AutoScalperEnabled = entity.AutoScalperEnabled,
            PaperTrading = entity.PaperTrading,
            ExecuteTrades = entity.ExecuteTrades,
            KillSwitch = entity.KillSwitch,
            IsEnabled = entity.IsEnabled,
            TradingEnabled = entity.TradingEnabled,
            MaxSpreadPct = entity.MaxSpreadPct,
            RsiMaxForEntry = entity.RsiMaxForEntry,
            MomentumMinPct = entity.MomentumMinPct,
            MinVolume24hUsd = entity.MinVolume24hUsd,
            MaxConcurrentPositions = entity.MaxConcurrentPositions
        };
    }
}