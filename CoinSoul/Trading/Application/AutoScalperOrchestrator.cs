using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;
using CoinSoul.Trading.Engine.V2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Application;

public sealed class AutoScalperOrchestrator
{
    private readonly ISettingsProvider _settingsProvider;
    private readonly IClock _clock;
    private readonly ITickLogger _tickLogger;
    private readonly IExecutionModeDecider _executionMode;
    private readonly CoinSoulDbContext _db;
    private readonly ILoggerFactory _loggerFactory;
    
    private readonly ExecutionPreconditionsValidator _preconditionsValidator;
    private readonly RegimeChangeDetector _regimeDetector;
    private readonly AutoScalperPositionManager _positionManager;
    private readonly MarketRegimeService _regimeService;
    private readonly RiskGuardService _riskGuard;
    private readonly IPortfolioService _portfolio;
    private readonly PortfolioRefreshService _portfolioRefresh;
    private readonly SymbolQueueManager _queue;
    private readonly PositionGuardService _positionGuard;
    private readonly SmartCooldownService _smartCooldown;
    private readonly ExecutionGuardService _executionGuard;
    private readonly OpportunityDetector _detector;
    private readonly PrecisionTradeExecutor _precisionExecutor;
    
    private readonly bool _enableDiagnosticLogging;

    public AutoScalperOrchestrator(
        ISettingsProvider settingsProvider,
        IClock clock,
        ITickLogger tickLogger,
        IExecutionModeDecider executionMode,
        CoinSoulDbContext db,
        ILoggerFactory loggerFactory,
        ExecutionPreconditionsValidator preconditionsValidator,
        RegimeChangeDetector regimeDetector,
        AutoScalperPositionManager positionManager,
        MarketRegimeService regimeService,
        RiskGuardService riskGuard,
        IPortfolioService portfolio,
        PortfolioRefreshService portfolioRefresh,
        SymbolQueueManager queue,
        PositionGuardService positionGuard,
        SmartCooldownService smartCooldown,
        ExecutionGuardService executionGuard,
        OpportunityDetector detector,
        PrecisionTradeExecutor precisionExecutor,
        Microsoft.Extensions.Configuration.IConfiguration configuration)
    {
        _settingsProvider = settingsProvider;
        _clock = clock;
        _tickLogger = tickLogger;
        _executionMode = executionMode;
        _db = db;
        _loggerFactory = loggerFactory;
        _preconditionsValidator = preconditionsValidator;
        _regimeDetector = regimeDetector;
        _positionManager = positionManager;
        _regimeService = regimeService;
        _riskGuard = riskGuard;
        _portfolio = portfolio;
        _portfolioRefresh = portfolioRefresh;
        _queue = queue;
        _positionGuard = positionGuard;
        _smartCooldown = smartCooldown;
        _executionGuard = executionGuard;
        _detector = detector;
        _precisionExecutor = precisionExecutor;
        _enableDiagnosticLogging = configuration.GetValue<bool>("EnableDiagnosticLogging", false);
    }

    public async Task<TickResult> RunTickAsync(
        BotState state,
        string correlationId,
        CancellationToken ct)
    {
        var context = new TickContext
        {
            CorrelationId = correlationId,
            TickStartUtc = _clock.UtcNow,
            CurrentStage = "Start",
            Metrics = new Dictionary<string, object>()
        };

        try
        {
            _tickLogger.LogCritical(
                "⚡ [PIPELINE_ENTERED] RunTickAsync started | Correlation={Correlation}, Diagnostic={Diag}",
                correlationId, _enableDiagnosticLogging);

            // STAGE 1: Load Settings
            context.CurrentStage = "LoadSettings";
            var settingsSnapshot = await _settingsProvider.GetSettingsSnapshotAsync(ct);

            if (settingsSnapshot == null)
            {
                _tickLogger.LogError(new Exception("BotSettings not found"),
                    "[STRATEGY_BLOCK] Reason=BotSettings not found, Correlation={Correlation}",
                    correlationId);

                return CreateTickResult(context, false, "SETTINGS_MISSING", null);
            }

            if (_enableDiagnosticLogging)
            {
                _tickLogger.LogWarning(
                    "[DIAG_SETTINGS] TargetUsd={Target}, MinUsd={Min}, MaxOpen={Max}, ExecuteTrades={Exec}, TradingEnabled={Trade}, Correlation={Correlation}",
                    settingsSnapshot.TargetUsdPerTrade,
                    settingsSnapshot.MinUsdPerTrade,
                    settingsSnapshot.MaxOpenTrades,
                    settingsSnapshot.ExecuteTrades,
                    settingsSnapshot.TradingEnabled,
                    correlationId);
            }

            // ✅ CRITICAL FIX: Synchronize BotState from settingsSnapshot
            // This ensures guards see the EXACT same state as settings indicate
            var shouldBeRunning = settingsSnapshot.TradingEnabled 
                && !settingsSnapshot.KillSwitch 
                && settingsSnapshot.AutoScalperEnabled;

            if (shouldBeRunning)
            {
                state.MarkRunning();
            }
            else
            {
                state.MarkStopped();
            }

            _tickLogger.LogCritical(
                "[ORCH_STATE_SYNC] TradingEnabled={TE} KillSwitch={KS} AutoScalperEnabled={AS} → Status={Status} IsRunning={IsRun} | Correlation={Correlation}",
                settingsSnapshot.TradingEnabled,
                settingsSnapshot.KillSwitch,
                settingsSnapshot.AutoScalperEnabled,
                state.Status,
                state.Status == BotStatus.Running, // FIX: Use Status comparison instead of IsRunning
                correlationId);

            // STAGE 2: Validate Guards
            context.CurrentStage = "ValidateGuards";
            _tickLogger.LogDebug("[STAGE] ValidateGuards | Correlation={Correlation}", correlationId);

            var guardResult = await _preconditionsValidator.ValidateExecutionPreconditionsAsync(
                settingsSnapshot, state, ct);

            if (!guardResult.Allowed)
            {
                _tickLogger.LogWarning("[GUARD_BLOCK] Code={Code}, Correlation={Correlation}",
                    guardResult.Code, correlationId);

                state.AddLog("WARN", $"[GUARD_BLOCK] {guardResult.Code}: {guardResult.Message}");
                return CreateTickResult(context, false, guardResult.Code, null);
            }

            // STAGE 3: Manage Positions
            context.CurrentStage = "ManagePositions";
            _tickLogger.LogDebug("[STAGE] ManagePositions | Correlation={Correlation}", correlationId);
            await _positionManager.ManageAsync(state, ct);

            // STAGE 4: Regime Decision
            context.CurrentStage = "RegimeDecision";
            _tickLogger.LogDebug("[STAGE] RegimeDecision | Correlation={Correlation}", correlationId);
            var nowUtc = _clock.UtcNowOffset;
            var regimeDecision = await _regimeService.GetDecisionAsync(nowUtc, ct);

            if (_enableDiagnosticLogging)
            {
                _tickLogger.LogWarning(
                    "[DIAG_REGIME] Regime={Regime}, AllowedToTrade={Allow}, RiskMultiplier={Risk:F2}, TpMultiplier={TP:F2}, Correlation={Correlation}",
                    regimeDecision.Regime,
                    regimeDecision.AllowedToTrade,
                    regimeDecision.RiskMultiplier,
                    regimeDecision.TpMultiplier,
                    correlationId);
            }

            var regimeSnapshot = new RegimeSnapshot(
                regimeDecision.Regime,
                regimeDecision.RiskMultiplier,
                regimeDecision.TpMultiplier,
                regimeDecision.AllowedToTrade,
                regimeDecision.Reason,
                _clock.UtcNow);

            if (_regimeDetector.ShouldLog(regimeSnapshot, logIntervalMinutes: 5))
            {
                _tickLogger.LogInformation(
                    "[MARKET_REGIME] Regime={Regime}, AllowTrade={Allow}, Risk={Risk:F2}, TP={TP:F2}, Correlation={Correlation}",
                    regimeDecision.Regime,
                    regimeDecision.AllowedToTrade,
                    regimeDecision.RiskMultiplier,
                    regimeDecision.TpMultiplier,
                    correlationId);

                state.AddLog("INFO", $"[REGIME] {regimeDecision.Regime} risk={regimeDecision.RiskMultiplier:0.00}");
                await _tickLogger.LogRegimeEventAsync(
                    new MarketRegimeDecision
                    {
                        AllowedToTrade = regimeDecision.AllowedToTrade,
                        Regime = regimeDecision.Regime,
                        RiskMultiplier = regimeDecision.RiskMultiplier,
                        TpMultiplier = regimeDecision.TpMultiplier,
                        Reason = regimeDecision.Reason,
                        AsOfUtc = regimeDecision.AsOfUtc
                    },
                    ct);
            }

            if (!regimeDecision.AllowedToTrade)
            {
                _tickLogger.LogWarning("[REGIME_BLOCK] Regime={Regime}, Correlation={Correlation}",
                    regimeDecision.Regime, correlationId);

                state.AddLog("WARN", $"[REGIME_BLOCK] {regimeDecision.Regime}");
                return CreateTickResult(context, false, $"REGIME_{regimeDecision.Regime}", null);
            }

            // STAGE 5: Risk Guard
            context.CurrentStage = "RiskGuard";
            _tickLogger.LogDebug("[STAGE] RiskGuard | Correlation={Correlation}", correlationId);
            var riskCheck = await _riskGuard.CanEnterNewTradeAsync(ct);

            if (_enableDiagnosticLogging)
            {
                _tickLogger.LogWarning(
                    "[DIAG_RISK_GUARD] CanEnter={CanEnter}, Reason={Reason}, Correlation={Correlation}",
                    riskCheck.CanEnter,
                    riskCheck.Reason,
                    correlationId);
            }

            if (!riskCheck.CanEnter)
            {
                _tickLogger.LogWarning("[RISK_GUARD] Reason={Reason}, Correlation={Correlation}",
                    riskCheck.Reason, correlationId);

                state.AddLog("WARN", $"[RISK_GUARD] {riskCheck.Reason}");
                return CreateTickResult(context, false, "RISK_GUARD", null);
            }

            // STAGE 6: Capital Calculation
            context.CurrentStage = "CapitalCalc";
            _tickLogger.LogDebug("[STAGE] CapitalCalc | Correlation={Correlation}", correlationId);
            var baseCapital = settingsSnapshot.TargetUsdPerTrade > 0
                ? settingsSnapshot.TargetUsdPerTrade
                : settingsSnapshot.CapitalPerTradeUsdt > 0
                    ? settingsSnapshot.CapitalPerTradeUsdt
                    : 25m;

            var effectiveCapital = Math.Max(baseCapital, baseCapital * regimeDecision.RiskMultiplier);

            var portfolioData = await _portfolio.GetPortfolioAsync(ct);
            var freeUsdt = portfolioData.FreeUsdt;

            if (_enableDiagnosticLogging)
            {
                _tickLogger.LogWarning(
                    "[DIAG_CAPITAL] FreeUSDT=${Free:N2}, BaseCapital=${Base:N2}, EffectiveCapital=${Eff:N2}, Correlation={Correlation}",
                    freeUsdt,
                    baseCapital,
                    effectiveCapital,
                    correlationId);
            }

            if (effectiveCapital < 10m)
            {
                _tickLogger.LogWarning("[CAPITAL_TOO_SMALL] Effective=${Size:N2}, Correlation={Correlation}",
                    effectiveCapital, correlationId);

                return CreateTickResult(context, false, "CAPITAL_TOO_SMALL", null);
            }

            context.CurrentStage = "CapitalAvailability";
            var capitalCheck = await _portfolioRefresh.CheckCapitalAvailabilityAsync(
                settingsSnapshot.TargetUsdPerTrade, settingsSnapshot, ct);

            if (_enableDiagnosticLogging)
            {
                _tickLogger.LogWarning(
                    "[DIAG_CAPITAL_CHECK] Allowed={Allow}, AvailableUsdt=${Available:N2}, Correlation={Correlation}",
                    capitalCheck.Allowed,
                    capitalCheck.AvailableUsdt,
                    correlationId);
            }

            if (!capitalCheck.Allowed)
            {
                _tickLogger.LogWarning("[CAPITAL_BLOCK] Reason={Reason}, Correlation={Correlation}",
                    capitalCheck.Reason, correlationId);

                state.AddLog("WARN", $"[CAPITAL_BLOCK] {capitalCheck.Reason}");
                return CreateTickResult(context, false, "CAPITAL_CHECK_FAILED", null);
            }

            // STAGE 7: Queue Pull
            context.CurrentStage = "QueuePull";
            _tickLogger.LogInformation("🔍 [QUEUE_PULL] Pulling from queue, Correlation={Correlation}",
                correlationId);

            var q = await _queue.DequeueAsync(
                state.Settings,
                msg => _tickLogger.LogDebug("[QUEUE_LOG] {Message}", msg),
                ct);

            if (q is null || string.IsNullOrWhiteSpace(q.Symbol))
            {
                _tickLogger.LogWarning("[QUEUE_EMPTY] No opportunities in queue. Attempting on-demand scan, Correlation={Correlation}",
                    correlationId);

                // ✅ Production hardening: if background scanner didn't enqueue yet, try a quick scan here.
                // This avoids a "stuck bot" when the queue is empty due to scanner delay / failure.
                try
                {
                    var takeTop = Math.Clamp(state.Settings.TopSymbolsCount, 5, 50);
                    var (candidates, diag) = await _detector.ScanTopAsync(
                        settings: state.Settings,
                        takeTop: takeTop,
                        minScanSeconds: 0,
                        ct: ct);

                    if (candidates.Count > 0)
                    {
                        // SymbolQueueManager enforces MaxQueueSize internally.
                        _queue.EnqueueBatch(
                            candidates.Select(c => new SymbolQueueManager.QueuedSymbol(
                                Symbol: c.Symbol,
                                Score: c.Score,
                                Reason: c.Reason)),
                            correlationId);

                        _tickLogger.LogInformation("[QUEUE_REFRESH_OK] Enqueued {Count} candidates (top={Top}) | Prefilter={Prefilter} Deep={Deep} Total={Total}ms, Correlation={Correlation}",
                            candidates.Count, takeTop,
                            diag.PrefilterCount, diag.DeepAnalysisCount, (int)diag.TotalMs,
                            correlationId);

                        // try dequeue again immediately
                        q = await _queue.DequeueAsync(
                            state.Settings,
                            msg => _tickLogger.LogDebug("[QUEUE_LOG] {Message}", msg),
                            ct);
                    }
                    else
                    {
                        _tickLogger.LogDebug("[QUEUE_REFRESH_EMPTY] Scan returned 0 candidates | Reason={Reason}, Correlation={Correlation}",
                            diag.Reason, correlationId);
                    }
                }
                catch (Exception ex)
                {
                    _tickLogger.LogError(ex, "[QUEUE_REFRESH_FAIL] On-demand scan failed, Correlation={Correlation}",
                        correlationId);
                }

                if (q is null || string.IsNullOrWhiteSpace(q.Symbol))
                    return CreateTickResult(context, false, "QUEUE_EMPTY", null);
            }

            var symbol = q.Symbol;
            context.SetMetric("symbol", symbol);
            context.SetMetric("score", q.Score);

            _tickLogger.LogInformation("🎯 [EVALUATING_SYMBOL] {Symbol}, Score={Score:F1}, Correlation={Correlation}",
                symbol, q.Score, correlationId);

            // STAGE 8: Position Guard
            context.CurrentStage = "PositionGuard";
            _tickLogger.LogDebug("[STAGE] PositionGuard | Symbol={Symbol}, Correlation={Correlation}",
                symbol, correlationId);
            var positionCheck = await _positionGuard.CanOpenNewPositionAsync(symbol, ct);

            if (!positionCheck.CanOpen)
            {
                _tickLogger.LogWarning("[ENTRY_SKIPPED] {Symbol} - PositionGuard: {Reason}, Correlation={Correlation}",
                    symbol, positionCheck.Reason, correlationId);

                _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(5));
                return CreateTickResult(context, false, positionCheck.BlockReason, symbol);
            }

            // STAGE 9: Cooldown Check
            context.CurrentStage = "CooldownCheck";
            _tickLogger.LogDebug("[STAGE] CooldownCheck | Symbol={Symbol}, Correlation={Correlation}",
                symbol, correlationId);
            var cooldownCheck = await _smartCooldown.CanEnterAsync(symbol, nowUtc, ct);

            if (!cooldownCheck.Allowed)
            {
                _tickLogger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Cooldown: {Reason}, Correlation={Correlation}",
                    symbol, cooldownCheck.Reason, correlationId);

                await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, cooldownCheck.Reason, ct);
                return CreateTickResult(context, false, "COOLDOWN", symbol);
            }

            // STAGE 10: Spike Check
            context.CurrentStage = "SpikeCheck";
            _tickLogger.LogDebug("[STAGE] SpikeCheck | Symbol={Symbol}, Correlation={Correlation}",
                symbol, correlationId);
            var spikeCheck = await _smartCooldown.CheckSpikeBlockAsync(symbol, nowUtc, ct);

            if (!spikeCheck.Allowed)
            {
                _tickLogger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Spike: {Reason}, Correlation={Correlation}",
                    symbol, spikeCheck.Reason, correlationId);

                await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, spikeCheck.Reason, ct);
                return CreateTickResult(context, false, "SPIKE_BLOCK", symbol);
            }

            // STAGE 11: Acquire Lock
            context.CurrentStage = "AcquireLock";
            _tickLogger.LogDebug("[STAGE] AcquireLock | Symbol={Symbol}, Correlation={Correlation}",
                symbol, correlationId);
            var lockAcquired = await _executionGuard.TryAcquireSymbolLockAsync(symbol, "ENTRY", ct);

            if (!lockAcquired)
            {
                _tickLogger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Lock busy, Correlation={Correlation}",
                    symbol, correlationId);

                return CreateTickResult(context, false, "LOCK_BUSY", symbol);
            }

            try
            {
                // STAGE 12: Race Checks
                context.CurrentStage = "RaceChecks";
                _tickLogger.LogDebug("[STAGE] RaceChecks | Symbol={Symbol}, Correlation={Correlation}",
                    symbol, correlationId);
                var portfolioRecheck = await _portfolio.GetPortfolioAsync(ct);

                if (portfolioRecheck.FreeUsdt < effectiveCapital)
                {
                    _tickLogger.LogWarning("[RACE_BALANCE] FreeUsdt=${Free:N2} < Required=${Req:N2}, Correlation={Correlation}",
                        portfolioRecheck.FreeUsdt, effectiveCapital, correlationId);
                    return CreateTickResult(context, false, "RACE_BALANCE", symbol);
                }

                var activeRecheck = await _db.Positions.CountAsync(p => p.IsActive && p.IsOpen, ct);
                if (settingsSnapshot.MaxConcurrentPositions > 0 &&
                    activeRecheck >= settingsSnapshot.MaxConcurrentPositions)
                {
                    _tickLogger.LogWarning("[RACE_POSITIONS] Active={Active} >= Max={Max}, Correlation={Correlation}",
                        activeRecheck, settingsSnapshot.MaxConcurrentPositions, correlationId);
                    return CreateTickResult(context, false, "RACE_POSITIONS", symbol);
                }

                // STAGE 13: Confirm Entry
                context.CurrentStage = "ConfirmEntry";
                _tickLogger.LogDebug("[STAGE] ConfirmEntry | Symbol={Symbol}, Correlation={Correlation}",
                    symbol, correlationId);
                var confirmResult = await _detector.ConfirmEntryNowAsync(symbol, state.Settings, ct);

                if (_enableDiagnosticLogging)
                {
                    _tickLogger.LogWarning(
                        "[DIAG_CONFIRM_RESULT] Symbol={Symbol}, Ok={Ok}, Why={Why}, Correlation={Correlation}",
                        symbol, confirmResult.Ok, confirmResult.Why, correlationId);
                }

                if (!confirmResult.Ok)
                {
                    _tickLogger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Confirm: {Why}, Correlation={Correlation}",
                        symbol, confirmResult.Why, correlationId);

                    state.AddLog("WARN", $"[REJECT] {symbol}: {confirmResult.Why}");
                    await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, confirmResult.Why, ct);
                    return CreateTickResult(context, false, "CONFIRMATION_FAILED", symbol);
                }

                _tickLogger.LogInformation("✅ [ENTRY_SIGNAL_READY] {Symbol}, Correlation={Correlation}",
                    symbol, correlationId);

                state.AddLog("INFO", $"[PICK] {symbol} score={q.Score:0.0}");

                // STAGE 14: Execution Decision
                var shouldExecute = _executionMode.ShouldExecuteTrades(settingsSnapshot);

                if (_enableDiagnosticLogging)
                {
                    _tickLogger.LogWarning(
                        "[DIAG_EXECUTION] Symbol={Symbol}, ShouldExecute={Should}, EffectiveCapital=${Capital:N2}, Correlation={Correlation}",
                        symbol,
                        shouldExecute,
                        effectiveCapital,
                        correlationId);
                }

                if (!shouldExecute)
                {
                    var mode = !settingsSnapshot.ExecuteTrades ? "DRY_RUN" : "SHADOW";

                    _tickLogger.LogWarning("[{Mode}] {Symbol} - Would place order ${Size:N2}, Correlation={Correlation}",
                        mode, symbol, effectiveCapital, correlationId);

                    state.AddLog("INFO", $"[{mode}] {symbol} ${effectiveCapital:N2} NOT EXECUTED");

                    return CreateTickResult(context, true, mode, symbol);
                }

                // STAGE 15: Execute
                context.CurrentStage = "Execute";
                _tickLogger.LogInformation("🚀 [ENTRY_ATTEMPT] {Symbol}, Size=${Size:N2}, Correlation={Correlation}",
                    symbol, effectiveCapital, correlationId);

                var execResult = await _precisionExecutor.ExecutePrecisionTradeAsync(
                    symbol,
                    effectiveCapital,
                    settingsSnapshot,
                    regimeDecision,
                    (level, msg) => _tickLogger.LogDebug("[EXECUTOR] {Level}: {Message}", level, msg),
                    ct);

                if (execResult.Success)
                {
                    _tickLogger.LogInformation("🎉 [ENTRY_SUCCESS] {Symbol}, PositionId={Id}, Correlation={Correlation}",
                        symbol, execResult.PositionId, correlationId);

                    state.AddLog("TRADE", $"[ENTRY_SUCCESS] {symbol} PosId={execResult.PositionId}");
                    await _smartCooldown.RecordEntryFilledAsync(symbol, nowUtc, ct);

                    return CreateTickResult(context, true, null, symbol, execResult.PositionId);
                }
                else
                {
                    // ✅ CRITICAL FIX #4: Propagate full execution error to BlockReason
                    var errorMsg = execResult.Error ?? "Unknown execution error";
                    var safeError = errorMsg.Length > 200 ? errorMsg[..200] : errorMsg;
    
                    // Classify error type
                    var errorType = errorMsg.ToLowerInvariant() switch
                    {
                        var e when e.Contains("insufficient") || e.Contains("balance") => "INSUFFICIENT_BALANCE",
                        var e when e.Contains("filter") || e.Contains("lot_size") || e.Contains("min_notional") => "FILTER_FAILURE",
                        var e when e.Contains("lock") || e.Contains("busy") => "LOCK_BUSY",
                        var e when e.Contains("timeout") => "TIMEOUT",
                        var e when e.Contains("network") => "NETWORK_ERROR",
                        var e when e.Contains("oco") => "OCO_FAILURE",
                        var e when e.Contains("buy") => "BUY_FAILURE",
                        _ => "UNKNOWN"
                    };
                    
                    _tickLogger.LogError(
                        new Exception(errorMsg),
                        "❌ [ENTRY_FAIL] {Symbol}, ErrorType={ErrorType}, Error={Error}, Correlation={Correlation}",
                        symbol,
                        errorType,
                        errorMsg,
                        correlationId);

                    state.AddLog("ERROR", $"[ENTRY_FAIL] {symbol}: {errorMsg}");
                    await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, errorMsg, ct);

                    // ✅ CRITICAL FIX #4: Enhanced TickResult with full error details
                    var result = CreateTickResult(context, false, $"EXECUTION_FAILED: {safeError}", symbol);
                    result.DiagnosticData["ExecutionError"] = errorMsg;
                    result.DiagnosticData["ExecutionErrorType"] = errorType;
                    result.DiagnosticData["Symbol"] = symbol;
                    result.DiagnosticData["Stage"] = "Execute";
    
                    return result;
                }
            }
            finally
            {
                await _executionGuard.ReleaseSymbolLockAsync(symbol, "ENTRY", ct);
            }
        }
        catch (Exception ex)
        {
            _tickLogger.LogError(ex, "[TICK_EXCEPTION] Stage={Stage}, Correlation={Correlation}",
                context.CurrentStage, correlationId);

            state.AddLog("ERROR", $"[EXCEPTION] {context.CurrentStage}: {ex.Message}");
            return CreateTickResult(context, false, "EXCEPTION", null);
        }
    }

    private TickResult CreateTickResult(
        TickContext context,
        bool success,
        string? blockReason,
        string? symbol,
        int? positionId = null)
    {
        var result = new TickResult
        {
            Success = success,
            Stage = context.CurrentStage,
            BlockReason = blockReason,
            DiagnosticData = new Dictionary<string, object>
            {
                ["CorrelationId"] = context.CorrelationId,
                ["TickStartUtc"] = context.TickStartUtc,
                ["TickEndUtc"] = _clock.UtcNow,
                ["Symbol"] = symbol ?? "N/A",
                ["PositionId"] = positionId?.ToString() ?? "N/A",
                ["Metrics"] = context.Metrics
            }
        };

        EmitTickSummary(result);
        return result;
    }

    private void EmitTickSummary(TickResult result)
    {
        var correlationId = result.DiagnosticData.TryGetValue("CorrelationId", out var corr) ? corr : "N/A";
        var symbol = result.DiagnosticData.TryGetValue("Symbol", out var sym) ? sym : "N/A";
        var positionId = result.DiagnosticData.TryGetValue("PositionId", out var pos) ? pos : "N/A";
        
        var duration = 0.0;
        if (result.DiagnosticData.TryGetValue("TickStartUtc", out var start) && start is DateTime startTime &&
            result.DiagnosticData.TryGetValue("TickEndUtc", out var end) && end is DateTime endTime)
        {
            duration = (endTime - startTime).TotalMilliseconds;
        }

        _tickLogger.LogInformation(
            "[TICK_SUMMARY] Correlation={Correlation}, Stage={Stage}, Success={Success}, BlockReason={Reason}, Symbol={Symbol}, PositionId={PosId}, Duration={Duration}ms",
            correlationId,
            result.Stage,
            result.Success,
            result.BlockReason ?? "N/A",
            symbol,
            positionId,
            duration);
    }
    public async Task<TickResult> ExecuteTickAsync(CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var tickStart = _clock.UtcNow;

        _tickLogger.LogCritical(
            "[ORCH_ENTER] ExecuteTickAsync invoked | CorrelationId={Correlation}",
            correlationId);

        try
        {
            // STAGE: Load Settings
            var settingsSnapshot = await _settingsProvider.GetSettingsSnapshotAsync(ct);

            if (settingsSnapshot == null)
            {
                _tickLogger.LogError(
                    new Exception("Settings snapshot is null"),
                    "[ORCH_BLOCK] Settings snapshot is null | CorrelationId={Correlation}",
                    correlationId);

                return new TickResult
                {
                    Success = false,
                    Stage = "LoadSettings",
                    BlockReason = "SETTINGS_NULL",
                    DiagnosticData = new Dictionary<string, object>
                    {
                        ["CorrelationId"] = correlationId,
                        ["TickStartUtc"] = tickStart,
                        ["TickEndUtc"] = _clock.UtcNow,
                        ["OpenPositions"] = 0
                    }
                };
            }

            _tickLogger.LogInformation(
                "[ORCH_SETTINGS_LOADED] TradingEnabled={Enabled}, ExecuteTrades={Execute}, KillSwitch={Kill}, AutoScalperEnabled={Auto} | CorrelationId={Correlation}",
                settingsSnapshot.TradingEnabled,
                settingsSnapshot.ExecuteTrades,
                settingsSnapshot.KillSwitch,
                settingsSnapshot.AutoScalperEnabled,
                correlationId);

            // STAGE: Create BotState - FIX: Initialize IsRunning based on settings
            var state = new BotState();
            state.ApplySettings(ConvertToBotSettings(settingsSnapshot));
            
            // ✅ Explicitly set IsRunning based on settings flags
            if (settingsSnapshot.TradingEnabled 
                && !settingsSnapshot.KillSwitch 
                && settingsSnapshot.AutoScalperEnabled)
            {
                state.MarkRunning();
            }
            else
            {
                state.MarkStopped();
            }

            _tickLogger.LogInformation(
                "[ORCH_STATE_INIT] BotState.IsRunning={IsRunning} | CorrelationId={Correlation}",
                state.Status == BotStatus.Running,
                correlationId);

            _tickLogger.LogInformation(
                "[ORCH_DELEGATE] Delegating to RunTickAsync | CorrelationId={Correlation}",
                correlationId);

            // STAGE: Delegate to full pipeline
            var pipelineResult = await RunTickAsync(state, correlationId, ct);

            _tickLogger.LogInformation(
                "[ORCH_COMPLETE] Pipeline returned | Stage={Stage}, Success={Success}, BlockReason={Reason} | CorrelationId={Correlation}",
                pipelineResult.Stage,
                pipelineResult.Success,
                pipelineResult.BlockReason ?? "N/A",
                correlationId);

            // ✅ Add diagnostic data for adaptive scheduler
            var openPositions = await GetOpenPositionsAsync(ct);

            if (!pipelineResult.DiagnosticData.ContainsKey("OpenPositions"))
            {
                pipelineResult.DiagnosticData["OpenPositions"] = openPositions.Count;
            }

            if (!pipelineResult.DiagnosticData.ContainsKey("CorrelationId"))
            {
                pipelineResult.DiagnosticData["CorrelationId"] = correlationId;
            }

            return pipelineResult;
        }
        catch (Exception ex)
        {
            _tickLogger.LogError(ex,
                "[ORCH_EXCEPTION] Orchestrator failed | CorrelationId={Correlation}",
                correlationId);

            return new TickResult
            {
                Success = false,
                Stage = "OrchestratorException",
                BlockReason = $"EXCEPTION: {ex.Message}",
                DiagnosticData = new Dictionary<string, object>
                {
                    ["CorrelationId"] = correlationId,
                    ["TickStartUtc"] = tickStart,
                    ["TickEndUtc"] = _clock.UtcNow,
                    ["ExceptionType"] = ex.GetType().Name,
                    ["OpenPositions"] = 0
                }
            };
        }
    }

    private async Task<List<PositionEntity>> GetOpenPositionsAsync(CancellationToken ct)
    {
        return await _db.Positions
            .Where(p => p.IsActive && p.IsOpen)
            .ToListAsync(ct);
    }

    private BotSettings ConvertToBotSettings(BotSettingsEntity entity)
    {
        // Map all relevant properties from BotSettingsEntity to BotSettings.
        // Fix: Convert string to TradeMode enum.
        return new BotSettings
        {
            TradeMode = Enum.TryParse<TradeMode>(entity.TradeMode, out var mode) ? mode : TradeMode.Spot,
            ExecuteTrades = entity.ExecuteTrades,
            KillSwitch = entity.KillSwitch,
            TargetUsdPerTrade = entity.TargetUsdPerTrade,
            MinUsdPerTrade = entity.MinUsdPerTrade,
            MaxOpenTrades = entity.MaxOpenTrades,
            TradingEnabled = entity.TradingEnabled,
            CapitalPerTradeUsdt = entity.CapitalPerTradeUsdt,
            MaxConcurrentPositions = entity.MaxConcurrentPositions,
            // ... map other properties as needed
        };
    }
}