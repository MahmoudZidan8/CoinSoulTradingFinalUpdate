using CoinSoul.Entities;
using CoinSoul.Trading.Core;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.V2;

/// <summary>
/// Production Hardened Guard Engine - Returns structured decisions instead of silent returns
/// </summary>
public sealed class GuardEngine
{
    private readonly ILogger<GuardEngine> _logger;

    public GuardEngine(ILogger<GuardEngine> logger)
    {
        _logger = logger;
    }

    public GuardResult CheckPreScan(
        BotSettingsEntity settings,
        BotState state,
        int openCount,
        decimal freeUsdt)
    {
        var now = DateTime.UtcNow;

        // Guard 1: StopUntilUtc
        if (settings.StopUntilUtc.HasValue && settings.StopUntilUtc.Value > now)
        {
            return GuardResult.Block("RISK_STOP", 
                $"Trading blocked until {settings.StopUntilUtc:yyyy-MM-dd HH:mm:ss} UTC",
                new { stopUntil = settings.StopUntilUtc });
        }

        // Guard 2: PauseUntilUtc
        if (settings.PauseUntilUtc.HasValue && settings.PauseUntilUtc.Value > now)
        {
            return GuardResult.Block("RISK_PAUSE",
                $"Trading paused until {settings.PauseUntilUtc:yyyy-MM-dd HH:mm:ss} UTC",
                new { pauseUntil = settings.PauseUntilUtc });
        }

        // Guard 3: BotStatus
        if (state.Status != BotStatus.Running)
        {
            return GuardResult.Block("BOT_NOT_RUNNING",
                $"BotState.Status={state.Status}",
                new { status = state.Status });
        }

        // Guard 4: StrategyMode
        if (state.Settings.StrategyMode != StrategyMode.AutoScalperD)
        {
            return GuardResult.Block("STRATEGY_MISMATCH",
                $"State.StrategyMode={state.Settings.StrategyMode} (not AutoScalperD)",
                new { mode = state.Settings.StrategyMode });
        }

        // Guard 5: AutoScalperEnabled
        if (!state.Settings.AutoScalperEnabled)
        {
            return GuardResult.Block("AUTOSCALPER_DISABLED",
                "State.Settings.AutoScalperEnabled=false",
                new { enabled = false });
        }

        // Guard 6: TradingEnabled
        if (!settings.TradingEnabled)
        {
            return GuardResult.Block("TRADING_DISABLED",
                "TradingEnabled=false",
                new { enabled = false });
        }

        // Guard 7: KillSwitch
        if (settings.KillSwitch)
        {
            return GuardResult.Block("KILL_SWITCH",
                "Emergency kill switch active",
                new { killSwitch = true });
        }

        // Guard 8: Config validation
        if (settings.TargetUsdPerTrade <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"TargetUsdPerTrade={settings.TargetUsdPerTrade} (must be > 0)",
                new { targetUsd = settings.TargetUsdPerTrade });
        }

        if (settings.MinUsdPerTrade <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"MinUsdPerTrade={settings.MinUsdPerTrade} (must be > 0)",
                new { minUsd = settings.MinUsdPerTrade });
        }

        if (settings.TakeProfitGrossPct <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"TakeProfitGrossPct={settings.TakeProfitGrossPct} (must be > 0)",
                new { tp = settings.TakeProfitGrossPct });
        }

        if (settings.StopLossGrossPct <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"StopLossGrossPct={settings.StopLossGrossPct} (must be > 0)",
                new { sl = settings.StopLossGrossPct });
        }

        // Guard 9: Max positions
        if (settings.MaxOpenTrades > 0 && openCount >= settings.MaxOpenTrades)
        {
            return GuardResult.Block("MAX_POSITIONS_REACHED",
                $"Current={openCount}, Max={settings.MaxOpenTrades}",
                new { current = openCount, max = settings.MaxOpenTrades });
        }

        // Guard 10: Balance
        var minRequired = settings.MinUsdPerTrade;
        if (freeUsdt < minRequired)
        {
            return GuardResult.Block("BALANCE_TOO_LOW",
                $"Free=${freeUsdt:N2} < Min=${minRequired:N2}",
                new { free = freeUsdt, required = minRequired });
        }

        return GuardResult.Allow();
    }
}

public sealed class GuardResult
{
    public bool Allowed { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public object? Details { get; init; }

    public static GuardResult Allow() => new() { Allowed = true, Code = "OK", Message = "All guards passed" };
    
    public static GuardResult Block(string code, string message, object? details = null)
        => new() { Allowed = false, Code = code, Message = message, Details = details };
}