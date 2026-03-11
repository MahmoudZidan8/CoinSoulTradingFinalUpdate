using CoinSoul.Entities;
using CoinSoul.Trading.Engine.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Safety;

/// <summary>
/// Safety gate that prevents accidental live trading
/// Requires two-step arming: ExecuteTrades=true AND LiveArmed=true
/// </summary>
public sealed class LiveTradingGate
{
    private readonly IEventWriter _eventWriter;
    private readonly IConfiguration _configuration;
    private readonly ILogger<LiveTradingGate> _logger;

    public LiveTradingGate(
        IEventWriter eventWriter,
        IConfiguration configuration,
        ILogger<LiveTradingGate> logger)
    {
        _eventWriter = eventWriter;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Checks if live trading is armed and ready
    /// Returns (IsLive, BlockReason)
    /// </summary>
    public async Task<(bool IsLive, string? BlockReason)> CheckLiveReadyAsync(
        BotSettingsEntity settings,
        string correlationId,
        CancellationToken ct)
    {
        // ✅ Condition 1: ExecuteTrades must be true
        if (!settings.ExecuteTrades)
        {
            return (false, "ExecuteTrades=false (dry-run mode)");
        }

        // ✅ Condition 2: KillSwitch must be false
        if (settings.KillSwitch)
        {
            return (false, "KillSwitch=true (emergency stop)");
        }

        // ✅ Condition 3: Event observability must be enabled
        if (!_eventWriter.IsEnabled)
        {
            await _eventWriter.WriteAsync(
                "LIVE_NOT_ARMED",
                "Cannot enable live trading: Observability:EnableDbEvents=false. Enable event logging first.",
                "ERROR",
                correlationId: correlationId,
                ct: ct);

            return (false, "Observability:EnableDbEvents=false (no event logging)");
        }

        // ✅ Condition 4: LiveArmed flag (DB or config)
        var liveArmedDb = settings.LiveArmed ?? false;
        var liveArmedConfig = _configuration.GetValue<bool>("Trading:LiveArmed", false);
        var isLiveArmed = liveArmedDb || liveArmedConfig;

        if (!isLiveArmed)
        {
            await _eventWriter.WriteAsync(
                "LIVE_NOT_ARMED",
                "Cannot enable live trading: LiveArmed=false. Set BotSettings.LiveArmed=true or Trading:LiveArmed=true in appsettings to arm live trading.",
                "WARN",
                correlationId: correlationId,
                ct: ct);

            return (false, "LiveArmed=false (not armed for live trading)");
        }

        // ✅ All conditions met - LIVE TRADING ARMED
        _logger.LogWarning("[LIVE_TRADING_ARMED] ⚠️ LIVE TRADING IS ACTIVE ⚠️");

        return (true, null);
    }
}