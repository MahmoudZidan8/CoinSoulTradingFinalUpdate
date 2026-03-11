using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.V2;

public sealed class ExecutionPreconditionsValidator
{
    private readonly CoinSoulDbContext _db;
    private readonly IPortfolioService _portfolio;
    private readonly ILogger<ExecutionPreconditionsValidator> _logger;

    public ExecutionPreconditionsValidator(
        CoinSoulDbContext db,
        IPortfolioService portfolio,
        ILogger<ExecutionPreconditionsValidator> logger)
    {
        _db = db;
        _portfolio = portfolio;
        _logger = logger;
    }

    public async Task<GuardResult> ValidateExecutionPreconditionsAsync(
        BotSettingsEntity settingsSnapshot,
        BotState state,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // Guard 1: StopUntilUtc
        if (settingsSnapshot.StopUntilUtc.HasValue && settingsSnapshot.StopUntilUtc.Value > now)
        {
            return GuardResult.Block("RISK_STOP",
                $"Trading blocked until {settingsSnapshot.StopUntilUtc:yyyy-MM-dd HH:mm:ss} UTC",
                new { stopUntil = settingsSnapshot.StopUntilUtc });
        }

        // Guard 2: PauseUntilUtc
        if (settingsSnapshot.PauseUntilUtc.HasValue && settingsSnapshot.PauseUntilUtc.Value > now)
        {
            return GuardResult.Block("RISK_PAUSE",
                $"Trading paused until {settingsSnapshot.PauseUntilUtc:yyyy-MM-dd HH:mm:ss} UTC",
                new { pauseUntil = settingsSnapshot.PauseUntilUtc });
        }

        // Guard 3: BotStatus
        if (state.Status != BotStatus.Running)
        {
            return GuardResult.Block("BOT_NOT_RUNNING",
                $"BotState.Status={state.Status}",
                new { status = state.Status.ToString() });
        }

        // Guard 4: StrategyMode
        if (state.Settings.StrategyMode != StrategyMode.AutoScalperD)
        {
            return GuardResult.Block("STRATEGY_MISMATCH",
                $"State.StrategyMode={state.Settings.StrategyMode} (not AutoScalperD)",
                new { mode = state.Settings.StrategyMode.ToString() });
        }

        // Guard 5: AutoScalperEnabled
        if (!state.Settings.AutoScalperEnabled)
        {
            return GuardResult.Block("AUTOSCALPER_DISABLED",
                "State.Settings.AutoScalperEnabled=false",
                new { enabled = false });
        }

        // Guard 6: TradingEnabled
        if (!settingsSnapshot.TradingEnabled)
        {
            return GuardResult.Block("TRADING_DISABLED",
                "TradingEnabled=false",
                new { enabled = false });
        }

        // Guard 7: KillSwitch
        if (settingsSnapshot.KillSwitch)
        {
            return GuardResult.Block("KILL_SWITCH",
                "Emergency kill switch active",
                new { killSwitch = true });
        }

        // Guard 8: Config validation
        if (settingsSnapshot.TargetUsdPerTrade <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"TargetUsdPerTrade={settingsSnapshot.TargetUsdPerTrade} (must be > 0)",
                new { targetUsd = settingsSnapshot.TargetUsdPerTrade });
        }

        if (settingsSnapshot.MinUsdPerTrade <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"MinUsdPerTrade={settingsSnapshot.MinUsdPerTrade} (must be > 0)",
                new { minUsd = settingsSnapshot.MinUsdPerTrade });
        }

        if (settingsSnapshot.TakeProfitGrossPct <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"TakeProfitGrossPct={settingsSnapshot.TakeProfitGrossPct} (must be > 0)",
                new { tp = settingsSnapshot.TakeProfitGrossPct });
        }

        if (settingsSnapshot.StopLossGrossPct <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"StopLossGrossPct={settingsSnapshot.StopLossGrossPct} (must be > 0)",
                new { sl = settingsSnapshot.StopLossGrossPct });
        }

        // Guard 9: Max positions
        var openCount = await _db.Positions.CountAsync(p => p.IsOpen, ct);
        if (settingsSnapshot.MaxOpenTrades > 0 && openCount >= settingsSnapshot.MaxOpenTrades)
        {
            return GuardResult.Block("MAX_POSITIONS_REACHED",
                $"Current={openCount}, Max={settingsSnapshot.MaxOpenTrades}",
                new { current = openCount, max = settingsSnapshot.MaxOpenTrades });
        }

        // Guard 10: Balance
        var portfolio = await _portfolio.GetPortfolioAsync(ct);
        var minRequired = settingsSnapshot.MinUsdPerTrade;
        
        if (portfolio.FreeUsdt < minRequired)
        {
            return GuardResult.Block("BALANCE_TOO_LOW",
                $"Free=${portfolio.FreeUsdt:N2} < Min=${minRequired:N2}",
                new { free = portfolio.FreeUsdt, required = minRequired });
        }

        return GuardResult.Allow();
    }
}