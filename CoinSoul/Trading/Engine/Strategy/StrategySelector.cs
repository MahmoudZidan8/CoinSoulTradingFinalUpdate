using CoinSoul.Entities;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Strategy;

/// <summary>
/// Selects the correct ITradingStrategy based on BotSettings.StrategyModeValue
/// Logs strategy selection as a DB event for observability
/// </summary>
public sealed class StrategySelector
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IEventWriter _eventWriter;
    private readonly ILogger<StrategySelector> _logger;

    public StrategySelector(
        IServiceProvider serviceProvider,
        IEventWriter eventWriter,
        ILogger<StrategySelector> logger)
    {
        _serviceProvider = serviceProvider;
        _eventWriter = eventWriter;
        _logger = logger;
    }

    public async Task<ITradingStrategy> SelectStrategyAsync(
        BotSettingsEntity settings,
        string correlationId,
        CancellationToken ct)
    {
        ITradingStrategy strategy;
        string strategyName;

        // ✅ Explicit routing based on StrategyModeValue
        switch (settings.StrategyModeValue)
        {
            case 1: // ManualA
                strategy = _serviceProvider.GetRequiredService<ManualStrategyA>();
                strategyName = "ManualStrategyA";
                break;

            case 3: // ScalperD
                strategy = _serviceProvider.GetRequiredService<ScalperStrategyD>();
                strategyName = "ScalperStrategyD";
                break;

            case 4: // AutoScalperD
                // ✅ FIX: Use ScalperStrategyD for AutoScalperD mode (StrategyModeValue=4)
                // AutoScalperStrategy is an orchestrator, not a strategy implementation
                strategy = _serviceProvider.GetRequiredService<ScalperStrategyD>();
                strategyName = "ScalperStrategyD (AutoScalperD mode)";
                break;

            default:
                _logger.LogWarning("[STRATEGY_SELECTOR] Unknown StrategyModeValue={Value}, defaulting to ScalperStrategyD",
                    settings.StrategyModeValue);
                strategy = _serviceProvider.GetRequiredService<ScalperStrategyD>();
                strategyName = "ScalperStrategyD (default)";
                break;
        }

        // ✅ Log strategy selection as DB event
        await _eventWriter.WriteAsync(
            "STRATEGY_SELECTED",
            $"Strategy: {strategyName}, ModeValue: {settings.StrategyModeValue}, AutoScalerEnabled: {settings.AutoScalperEnabled}",
            "INFO",
            correlationId: correlationId,
            ct: ct);

        _logger.LogInformation("[STRATEGY_SELECTED] {Strategy} for ModeValue={ModeValue}",
            strategyName, settings.StrategyModeValue);

        return strategy;
    }
}