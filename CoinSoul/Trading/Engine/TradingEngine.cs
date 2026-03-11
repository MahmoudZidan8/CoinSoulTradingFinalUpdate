using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine;

public sealed class TradingEngine : ITradingEngine
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TradingEngine> _logger;
    private readonly object _lock = new();
    private readonly BotState _state = new();

    public event Action? OnStateChanged;

    public TradingEngine(
        IServiceScopeFactory scopeFactory,
        ILogger<TradingEngine> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _state.AddLog("INFO", "TradingEngine initialized.");
    }

    public BotState GetState() => _state;

    public async void Start()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();

        var entity = await db.BotSettings.FirstOrDefaultAsync();
        if (entity == null)
        {
            entity = new BotSettingsEntity
            {
                StrategyModeValue = (int)StrategyMode.AutoScalperD,
                AutoScalperEnabled = true,
                IsEnabled = true,
                IsRunning = true,
                LastStartUtc = DateTime.UtcNow
            };
            db.BotSettings.Add(entity);
        }
        else
        {
            entity.IsRunning = true;
            entity.LastStartUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        lock (_lock)
        {
            if (_state.Status != BotStatus.Running)
                _state.MarkRunning();
        }

        _logger.LogInformation("[BOT_START] IsRunning=true, AutoScalperEnabled={Auto}", 
            entity.AutoScalperEnabled);
        
        _state.AddLog("INFO", "Bot started (DB persisted).");
        OnStateChanged?.Invoke();
    }

    public async void Stop()
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();

        var entity = await db.BotSettings.FirstOrDefaultAsync();
        if (entity != null)
        {
            entity.IsRunning = false;
            entity.LastStopUtc = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        lock (_lock)
        {
            if (_state.Status == BotStatus.Running)
                _state.MarkStopped();
        }

        _logger.LogInformation("[BOT_STOP] IsRunning=false");
        _state.AddLog("WARN", "Bot stopped (DB persisted).");
        OnStateChanged?.Invoke();
    }

    public async Task EnqueueAsync(ITradingCommand command, CancellationToken ct = default)
    {
        try
        {
            switch (command)
            {
                case StartBotCommand:
                    Start();
                    break;

                case StopBotCommand:
                    Stop();
                    break;

                case UpdateBotSettingsCommand u:
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();
                        var entity = await db.BotSettings.FirstOrDefaultAsync(ct);

                        if (entity == null)
                        {
                            entity = new BotSettingsEntity();
                            db.BotSettings.Add(entity);
                        }

                        entity.TradeMode = u.Settings.TradeMode == TradeMode.Futures ? "Futures" : "Spot";
                        entity.StrategyModeValue = (int)u.Settings.StrategyMode;
                        entity.AutoScalperEnabled = u.Settings.AutoScalperEnabled;
                        entity.NetProfitTargetUsd = u.Settings.NetProfitTargetUsd;
                        entity.MaxTradeDurationSeconds = u.Settings.MaxTradeDurationSeconds;
                        entity.HardStopLossPct = u.Settings.HardStopLossPct;
                        entity.MaxSpreadPct = u.Settings.MaxSpreadPct;
                        entity.Min24hQuoteVolumeUsdt = u.Settings.Min24hQuoteVolumeUsdt;
                        entity.SlippageBufferUsd = u.Settings.SlippageBufferUsd;
                        entity.MaxUsdPerTrade = u.Settings.MaxUsdPerTrade;
                        entity.TickSeconds = u.Settings.TickSeconds;
                        entity.PaperTrading = u.Settings.PaperTrading;
                        entity.TimeExitMinutes = u.Settings.TimeExitMinutes;

                        await db.SaveChangesAsync(ct);
                        
                        _logger.LogInformation(
                            "[SETTINGS_UPDATE] AutoScalperEnabled={Auto}",
                            entity.AutoScalperEnabled);
                    }

                    lock (_lock)
                        _state.ApplySettings(u.Settings);

                    _state.AddLog("INFO", "Settings updated from UI.");
                    break;
            }
        }
        catch (Exception ex)
        {
            lock (_lock)
                _state.MarkError(ex.Message);
        }
        finally
        {
            OnStateChanged?.Invoke();
        }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            // ✅ LOG 1: TICK START
            var tickStartTime = DateTime.UtcNow;
            _logger.LogDebug("[TICK_START] {Time}", tickStartTime);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();

            var entity = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            
            if (entity == null)
            {
                _logger.LogError("[ENGINE_SETTINGS_MISSING] No BotSettings found in database");
                return;
            }

            // ✅ LOG 2: ENGINE STATE - COMPREHENSIVE
            _logger.LogInformation(
                "[ENGINE_STATE] IsRunning={Running}, IsEnabled={Enabled}, StrategyModeValue={StrategyMode}, AutoScalperEnabled={Auto}, ExecuteTrades={Exec}, TradingEnabled={Trade}",
                entity.IsRunning,
                entity.IsEnabled,
                entity.StrategyModeValue,
                entity.AutoScalperEnabled,
                entity.ExecuteTrades,
                entity.TradingEnabled);

            // ✅ CHECK 1: IsRunning
            if (!entity.IsRunning)
            {
                _logger.LogDebug("[ENGINE_SKIP] IsRunning=false - bot is stopped");
                lock (_lock)
                {
                    if (_state.Status == BotStatus.Running)
                        _state.MarkStopped();
                }
                return;
            }

            // ✅ CHECK 2: IsEnabled
            if (!entity.IsEnabled)
            {
                _logger.LogDebug("[ENGINE_SKIP] IsEnabled=false - bot is disabled");
                lock (_lock)
                {
                    if (_state.Status == BotStatus.Running)
                        _state.MarkStopped();
                }
                return;
            }

            // ✅ PART 3: COMPLETE SAFE MAPPING - All Properties
            var settings = new BotSettings
            {
                // Core Mode
                TradeMode = entity.TradeMode == "Futures" ? TradeMode.Futures : TradeMode.Spot,
                StrategyMode = (StrategyMode)entity.StrategyModeValue,
                AutoScalperEnabled = entity.AutoScalperEnabled,
                PaperTrading = entity.PaperTrading,

                // ✅ CRITICAL SAFETY FLAGS
                ExecuteTrades = entity.ExecuteTrades,
                KillSwitch = entity.KillSwitch,
                IsEnabled = entity.IsEnabled,
                TradingEnabled = entity.TradingEnabled,

                // ✅ Capital & Sizing
                TargetUsdPerTrade = entity.TargetUsdPerTrade,
                MinUsdPerTrade = entity.MinUsdPerTrade,
                TradeSizeUsd = entity.TradeSizeUsd,
                CapitalPerTradeUsdt = entity.CapitalPerTradeUsdt,
                MinFreeBalanceUsdt = entity.MinFreeBalanceUsdt,
                MinFreeUsdtReserve = entity.MinFreeUsdtReserve,
                MinUsdtToOpenNewPosition = entity.MinUsdtToOpenNewPosition,
                MaxOpenTrades = entity.MaxOpenTrades,
                MaxConcurrentPositions = entity.MaxConcurrentPositions,
                PreventSameSymbolTwice = entity.PreventSameSymbolTwice,
                AllowMultipleSymbols = entity.AllowMultipleSymbols,
                BlockSameSymbolReentry = entity.BlockSameSymbolReentry,
                TradeHistoryTopSymbols = entity.TradeHistoryTopSymbols,

                // ✅ Targets & Exits
                TakeProfitGrossPct = entity.TakeProfitGrossPct,
                StopLossGrossPct = entity.StopLossGrossPct,
                NetProfitTargetUsd = entity.NetProfitTargetUsd,
                IncludeFeesInTP = entity.IncludeFeesInTP,

                // ✅ Timing
                MaxTradeDurationSeconds = entity.MaxTradeDurationSeconds,
                MaxTradeDurationMinutes = entity.MaxTradeDurationMinutes,
                TimeExitMinutes = entity.TimeExitMinutes,
                TickSeconds = entity.TickSeconds,

                // ✅ Risk & Safety
                HardStopLossPct = entity.HardStopLossPct,
                MaxAllowedEntrySlippagePct = entity.MaxAllowedEntrySlippagePct,
                MaxEntrySlippagePct = entity.MaxEntrySlippagePct,
                PauseUntilUtc = entity.PauseUntilUtc,
                StopUntilUtc = entity.StopUntilUtc,
                RiskGuardPause30MinPct = entity.RiskGuardPause30MinPct,
                RiskGuardPause3HourPct = entity.RiskGuardPause3HourPct,
                RiskGuardStopUntilMidnightPct = entity.RiskGuardStopUntilMidnightPct,

                // ✅ Spread & Slippage
                MaxSpreadPct = entity.MaxSpreadPct,
                SpreadBufferPct = entity.SpreadBufferPct,
                SlippageBufferPct = entity.SlippageBufferPct,
                SlippageBufferUsd = entity.SlippageBufferUsd,

                // ✅ Fees
                MakerFeeRate = entity.MakerFeeRate,
                TakerFeeRate = entity.TakerFeeRate,

                // ✅ Volume & Liquidity
                Min24hQuoteVolumeUsdt = entity.Min24hQuoteVolumeUsdt,
                MinVolume24hUsd = entity.MinVolume24hUsd,

                // ✅ Execution
                UseOcoExit = entity.UseOcoExit,
                OcoStopLimitBufferPct = entity.OcoStopLimitBufferPct,
                PlaceSeparateTpSlIfOcoFails = entity.PlaceSeparateTpSlIfOcoFails,
                OcoRetryAttempts = entity.OcoRetryAttempts,
                UseLimitMakerEntry = entity.UseLimitMakerEntry,
                LimitMakerDiscountBps = entity.LimitMakerDiscountBps,
                LimitMakerTimeoutSeconds = entity.LimitMakerTimeoutSeconds,
                FallbackToMarketOnEntryTimeout = entity.FallbackToMarketOnEntryTimeout,
                QtyBufferPct = entity.QtyBufferPct,
                DustIgnoreUsdThreshold = entity.DustIgnoreUsdThreshold,

                // ✅ Cooldowns
                EntryCooldownSeconds = entity.EntryCooldownSeconds,
                CooldownAfterEntrySeconds = entity.CooldownAfterEntrySeconds,
                CooldownAfterLossSeconds = entity.CooldownAfterLossSeconds,
                CooldownSameSymbolSeconds = entity.CooldownSameSymbolSeconds,
                EnableSmartCooldown = entity.EnableSmartCooldown,
                SmartCooldownMinutes = entity.SmartCooldownMinutes,
                MaxReentriesPerSymbolPerHour = entity.MaxReentriesPerSymbolPerHour,
                BlockRevengeTradingMinutes = entity.BlockRevengeTradingMinutes,
                EnableSpikeBlock = entity.EnableSpikeBlock,
                SpikeBlockAtrPct = entity.SpikeBlockAtrPct,
                SpikeBlock1mMovePct = entity.SpikeBlock1mMovePct,
                SpikeCheckLookbackMinutes = entity.SpikeCheckLookbackMinutes,

                // ✅ Market Regime
                EnableMarketRegimeFilter = entity.EnableMarketRegimeFilter,
                RegimeAnchorSymbol = entity.RegimeAnchorSymbol,
                RegimeTimeframe = entity.RegimeTimeframe,
                RegimeTimeframeMinutes = entity.RegimeTimeframeMinutes,
                RegimeLookbackBars = entity.RegimeLookbackBars,
                RegimeFastEmaPeriod = entity.RegimeFastEmaPeriod,
                RegimeSlowEmaPeriod = entity.RegimeSlowEmaPeriod,
                RegimeAtrPeriod = entity.RegimeAtrPeriod,
                RegimeAtrLookback = entity.RegimeAtrLookback,
                BtcEmaPeriod = entity.BtcEmaPeriod,
                SidewaysAtrPctThreshold = entity.SidewaysAtrPctThreshold,
                HighVolAtrPctThreshold = entity.HighVolAtrPctThreshold,
                TrendAtrPctThreshold = entity.TrendAtrPctThreshold,
                RegimeRiskScale = entity.RegimeRiskScale,
                RegimeTpScale = entity.RegimeTpScale,
                BlockTradingOnCrash = entity.BlockTradingOnCrash,
                Crash1hMovePct = entity.Crash1hMovePct,
                CrashLookbackMinutes = entity.CrashLookbackMinutes,
                RiskMultBull = entity.RiskMultBull,
                RiskMultBear = entity.RiskMultBear,
                RiskMultSideways = entity.RiskMultSideways,
                RiskMultCrash = entity.RiskMultCrash,
                TpMultBull = entity.TpMultBull,
                TpMultBear = entity.TpMultBear,
                TpMultSideways = entity.TpMultSideways,
                TpMultCrash = entity.TpMultCrash,
                ForceConservativeInBear = entity.ForceConservativeInBear,

                // ✅ Signal Filters
                RsiMaxForEntry = entity.RsiMaxForEntry,
                MomentumMinPct = entity.MomentumMinPct,
                RejectShortTermPeak = entity.RejectShortTermPeak,

                // ✅ Session Time
                TradingStartTime = entity.TradingStartTime,
                TradingEndTime = entity.TradingEndTime,

                // ✅ Reconciliation
                ReconcileIntervalSeconds = entity.ReconcileIntervalSeconds,
                BalanceRefreshCooldownMs = entity.BalanceRefreshCooldownMs,

                // Legacy
                MaxUsdPerTrade = entity.MaxUsdPerTrade
            };

            lock (_lock)
            {
                if (_state.Status != BotStatus.Running)
                    _state.MarkRunning();

                _state.ApplySettings(settings);
                _state.MarkTick();
            }

            // ✅ CHECK 3: Daily loss guard
            var dailyLoss = _state.DailyLossUsd;
            if (dailyLoss >= _state.Settings.DailyLossLimitUsd)
            {
                _logger.LogWarning(
                    "[ENGINE_SKIP] Daily loss guard triggered: Loss=${Loss:N2} >= Limit=${Limit:N2}",
                    dailyLoss, _state.Settings.DailyLossLimitUsd);
                
                _state.AddLog("WARN", $"[DAILY_LOSS_GUARD] dailyLoss={dailyLoss:0.00}$ >= limit={_state.Settings.DailyLossLimitUsd:0.00}$ => skip trading");
                OnStateChanged?.Invoke();
                return;
            }

            _state.AddLog("DEBUG", $"Tick#{_state.TickCount} Strategy={settings.StrategyMode} Enabled={settings.AutoScalperEnabled}");

            // ✅ LOG 3: STRATEGY MODE CHECK WITH NUMERIC VALUES
            var expectedStrategyMode = StrategyMode.AutoScalperD;
            var actualStrategyMode = settings.StrategyMode;
            var autoScalperValue = (int)StrategyMode.AutoScalperD; // Should be 4

            _logger.LogInformation(
                "[STRATEGY_MODE] StrategyModeValue={ActualValue} ({ActualName}), AutoScalperValue={AutoValue} (AutoScalperD), Match={Match}",
                (int)actualStrategyMode,
                actualStrategyMode.ToString(),
                autoScalperValue,
                actualStrategyMode == expectedStrategyMode);

            if (actualStrategyMode != expectedStrategyMode)
            {
                _logger.LogWarning(
                    "[STRATEGY_MISMATCH] Expected=AutoScalperD (value={ExpectedValue}), Actual={ActualName} (value={ActualValue}) - STRATEGY WILL NOT RUN",
                    (int)expectedStrategyMode,
                    actualStrategyMode.ToString(),
                    (int)actualStrategyMode);
                
                _state.AddLog("WARN", $"[STRATEGY_MISMATCH] Expected=AutoScalperD, Actual={actualStrategyMode}");
                return;
            }

            _logger.LogDebug("[ENGINE_STRATEGY_MATCH] Strategy mode is AutoScalperD (value={Value})", 
                (int)actualStrategyMode);

            // ✅ CHECK 4: AutoScalperEnabled
            if (!settings.AutoScalperEnabled)
            {
                _logger.LogWarning("[ENGINE_SKIP] AutoScalperEnabled=false - strategy is disabled");
                _state.AddLog("WARN", "[ENGINE_SKIP] AutoScalperEnabled=false");
                return;
            }

            _logger.LogInformation("[ENGINE_AUTOSCALPER_ENABLED] AutoScalperEnabled=true - ready to call strategy");

            // ✅ LOG 4: CALLING STRATEGY
            _logger.LogInformation("[ENGINE_CALLING_AUTOSCALPER] About to invoke AutoScalperStrategy.EvaluateAsync");

            var autoScalper = scope.ServiceProvider.GetRequiredService<AutoScalperStrategy>();
            
            if (autoScalper == null)
            {
                _logger.LogError("[ENGINE_ERROR] Failed to resolve AutoScalperStrategy from DI container");
                _state.AddLog("ERROR", "[ENGINE_ERROR] AutoScalperStrategy service not found");
                return;
            }

            _logger.LogDebug("[ENGINE_STRATEGY_RESOLVED] AutoScalperStrategy instance obtained");

            try
            {
                await autoScalper.EvaluateAsync(_state, ct);
                _logger.LogDebug("[ENGINE_STRATEGY_COMPLETE] AutoScalperStrategy.EvaluateAsync completed");
            }
            catch (Exception strategyEx)
            {
                _logger.LogError(strategyEx, "[ENGINE_STRATEGY_ERROR] AutoScalperStrategy.EvaluateAsync threw exception");
                _state.AddLog("ERROR", $"[STRATEGY_ERROR] {strategyEx.Message}");
            }

            OnStateChanged?.Invoke();
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("[ENGINE_CANCELLED] RunAsync cancelled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ENGINE_ERROR] RunAsync failed with exception");
            
            lock (_lock)
                _state.MarkError(ex.Message);

            OnStateChanged?.Invoke();
        }
    }

    public async Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();

        var closed = await db.Positions
            .Where(p => !p.IsOpen && p.ClosedAtUtc != null)
            .ToListAsync(ct);

        var wins = closed.Where(x => x.NetPnlUsdt > 0).ToList();
        var losses = closed.Where(x => x.NetPnlUsdt < 0).ToList();

        var grossWin = wins.Sum(x => x.NetPnlUsdt);
        var grossLoss = losses.Sum(x => Math.Abs(x.NetPnlUsdt));

        return new DashboardStats
        {
            TradesCount = closed.Count,
            NetPnlUsdt = closed.Sum(x => x.NetPnlUsdt),
            AvgWinUsdt = wins.Any() ? wins.Average(x => x.NetPnlUsdt) : 0,
            AvgLossUsdt = losses.Any() ? losses.Average(x => Math.Abs(x.NetPnlUsdt)) : 0,
            MaxDrawdownUsdt = losses.Any() ? losses.Min(x => x.NetPnlUsdt) : 0,
            ProfitFactor = grossLoss == 0 ? 0 : (grossWin / grossLoss),
            ExpectancyUsdt = closed.Any() ? closed.Sum(x => x.NetPnlUsdt) / closed.Count : 0
        };
    }

    public async Task TickAsync(BotState state, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CoinSoulDbContext>();
        var strategy = scope.ServiceProvider.GetRequiredService<AutoScalperStrategy>();

        try
        {
            await strategy.EvaluateAsync(state, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[TICK_ERROR] Strategy evaluation failed");
            state.AddLog("ERROR", $"[TICK_ERROR] {ex.Message}");
        }
    }
}

