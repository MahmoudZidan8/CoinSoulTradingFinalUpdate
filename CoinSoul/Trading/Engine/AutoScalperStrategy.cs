using CoinSoul.BinanceService.AutoServices.AccountDataService;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine.Observability;
using CoinSoul.Trading.Engine.V2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// ✅ PRODUCTION HOTFIX: Fixed uninitialized field _validQuoteAssets
/// </summary>
public sealed class AutoScalperStrategy : ITradingStrategy
{
    private readonly CoinSoulDbContext _db;
    private readonly ITradeExecutor _exec;
    private readonly IAutoAccountDataService _account;
    private readonly AutoScalperPositionManager _positionManager;
    private readonly SymbolQueueManager _queue;
    private readonly OpportunityDetector _detector;
    private readonly RiskGuardService _riskGuard;
    private readonly ExecutionGuardService _executionGuard;
    private readonly SlippageProtection _slippage;
    private readonly NetProfitTargetService _netProfit;
    private readonly PrecisionTradeExecutor _precisionExecutor;
    private readonly PositionGuardService _positionGuard;
    private readonly CapitalAllocationService _capitalAllocation;
    private readonly IPortfolioService _portfolio;
    private readonly SmartCooldownService _smartCooldown;
    private readonly MarketRegimeService _regimeService;
    private readonly PortfolioRefreshService _portfolioRefresh;
    private readonly ILogger<AutoScalperStrategy> _logger;
    private readonly ExecutionPreconditionsValidator _preconditionsValidator;
    private readonly RegimeChangeDetector _regimeDetector;

    private readonly bool _coreV2Enabled;
    private readonly bool _coreV2ShadowMode;
    private readonly bool _enableDiagnosticLogging;

    // ✅ CRITICAL FIX #2: Initialize _validQuoteAssets to prevent CS8618 and CS0649
    private readonly HashSet<string> _validQuoteAssets = new(StringComparer.OrdinalIgnoreCase)
    {
        "USDT", "BUSD", "USD"
    };

    public AutoScalperStrategy(
        CoinSoulDbContext db,
        ITradeExecutor exec,
        IAutoAccountDataService account,
        AutoScalperPositionManager positionManager,
        SymbolQueueManager queue,
        OpportunityDetector detector,
        RiskGuardService riskGuard,
        ExecutionGuardService executionGuard,
        SlippageProtection slippage,
        NetProfitTargetService netProfit,
        PrecisionTradeExecutor precisionExecutor,
        PositionGuardService positionGuard,
        CapitalAllocationService capitalAllocation,
        IPortfolioService portfolio,
        SmartCooldownService smartCooldown,
        MarketRegimeService regimeService,
        PortfolioRefreshService portfolioRefresh,
        ILogger<AutoScalperStrategy> logger,
        ExecutionPreconditionsValidator preconditionsValidator,
        RegimeChangeDetector regimeDetector,
        IConfiguration configuration)
    {
        _db = db;
        _exec = exec;
        _account = account;
        _positionManager = positionManager;
        _queue = queue;
        _detector = detector;
        _riskGuard = riskGuard;
        _executionGuard = executionGuard;
        _slippage = slippage;
        _netProfit = netProfit;
        _precisionExecutor = precisionExecutor;
        _positionGuard = positionGuard;
        _capitalAllocation = capitalAllocation;
        _portfolio = portfolio;
        _smartCooldown = smartCooldown;
        _regimeService = regimeService;
        _portfolioRefresh = portfolioRefresh;
        _logger = logger;
        _preconditionsValidator = preconditionsValidator;
        _regimeDetector = regimeDetector;
        
        _coreV2Enabled = Environment.GetEnvironmentVariable("COINSOUL_COREV2_ENABLED") == "true";
        _coreV2ShadowMode = Environment.GetEnvironmentVariable("COINSOUL_COREV2_SHADOW") == "true";
        _enableDiagnosticLogging = configuration.GetValue<bool>("EnableDiagnosticLogging", false);
    }

    public async Task EvaluateAsync(BotState state, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var tickStart = DateTime.UtcNow;
        var tickStage = "Start";

        try
        {
            _logger.LogCritical("⚡ [AUTOSCALPER_ENTERED] Correlation={Correlation}, Diagnostic={Diag}",
                correlationId, _enableDiagnosticLogging);

            // ✅ PHASE 1: LOAD SETTINGS (< 5ms with AsNoTracking)
            var settingsSnapshot = await _db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            
            if (settingsSnapshot == null)
            {
                tickStage = "LoadSettings";
                _logger.LogError("[STRATEGY_BLOCK] Reason=BotSettings not found, Correlation={Correlation}",
                    correlationId);
                
                EmitTickSummary(correlationId, tickStart, tickStage, false, "SETTINGS_MISSING", null);
                return;
            }

            tickStage = "ValidateGuards";

            // ✅ PHASE 2: GUARD VALIDATION (< 20ms)
            var guardResult = await _preconditionsValidator.ValidateExecutionPreconditionsAsync(
                settingsSnapshot, state, ct);

            if (!guardResult.Allowed)
            {
                _logger.LogWarning("[GUARD_BLOCK] Code={Code}, Correlation={Correlation}",
                    guardResult.Code, correlationId);
                
                EmitTickSummary(correlationId, tickStart, tickStage, false, guardResult.Code, null);
                return;
            }

            tickStage = "ManagePositions";
            await _positionManager.ManageAsync(state, ct);

            // ✅ PHASE 3: REGIME CHECK (< 10ms with caching)
            tickStage = "RegimeDecision";
            var nowUtc = DateTimeOffset.UtcNow;
            var regimeDecision = await _regimeService.GetDecisionAsync(nowUtc, ct);

            if (!regimeDecision.AllowedToTrade)
            {
                _logger.LogWarning("[REGIME_BLOCK] Regime={Regime}, Correlation={Correlation}",
                    regimeDecision.Regime, correlationId);
                
                EmitTickSummary(correlationId, tickStart, tickStage, false, $"REGIME_{regimeDecision.Regime}", null);
                return;
            }

            // ✅ PHASE 4: RISK GUARD (< 15ms)
            tickStage = "RiskGuard";
            var riskCheck = await _riskGuard.CanEnterNewTradeAsync(ct);
            
            if (!riskCheck.CanEnter)
            {
                _logger.LogWarning("[RISK_GUARD] Reason={Reason}, Correlation={Correlation}",
                    riskCheck.Reason, correlationId);
                
                EmitTickSummary(correlationId, tickStart, tickStage, false, "RISK_GUARD", null);
                return;
            }

            // ✅ PHASE 5: CAPITAL CALCULATION (< 10ms)
            tickStage = "CapitalCalc";
            var baseCapital = settingsSnapshot.TargetUsdPerTrade > 0 
                ? settingsSnapshot.TargetUsdPerTrade 
                : settingsSnapshot.CapitalPerTradeUsdt > 0 
                    ? settingsSnapshot.CapitalPerTradeUsdt 
                    : 25m;

            var effectiveCapital = Math.Max(baseCapital, baseCapital * regimeDecision.RiskMultiplier);

            if (effectiveCapital < 10m)
            {
                EmitTickSummary(correlationId, tickStart, tickStage, false, "CAPITAL_TOO_SMALL", null);
                return;
            }

            var capitalCheck = await _portfolioRefresh.CheckCapitalAvailabilityAsync(
                settingsSnapshot.TargetUsdPerTrade, settingsSnapshot, ct);

            if (!capitalCheck.Allowed)
            {
                EmitTickSummary(correlationId, tickStart, tickStage, false, "CAPITAL_CHECK_FAILED", null);
                return;
            }

            // ✅ PHASE 6: PULL FROM QUEUE (< 5ms - NO SCANNING)
            tickStage = "QueuePull";
            
            _logger.LogInformation("🔍 [QUEUE_PULL] Pulling from pre-scanned queue, Correlation={Correlation}", 
                correlationId);

            var q = await _queue.DequeueAsync(
                state.Settings,
                msg => _logger.LogDebug("[QUEUE_LOG] {Message}", msg),
                ct);

            if (q is null || string.IsNullOrWhiteSpace(q.Symbol))
            {
                _logger.LogDebug("[QUEUE_EMPTY] No opportunities in queue, Correlation={Correlation}", 
                    correlationId);
                
                EmitTickSummary(correlationId, tickStart, tickStage, false, "QUEUE_EMPTY", null);
                return;
            }

            var symbol = q.Symbol;

            tickStage = "EvaluatingSymbol";
            _logger.LogInformation("🎯 [EVALUATING_SYMBOL] {Symbol}, Score={Score:F1}, Correlation={Correlation}",
                symbol, q.Score, correlationId);

            // ✅ PHASE 7: POSITION GUARD (< 10ms)
            tickStage = "PositionGuard";
            var positionCheck = await _positionGuard.CanOpenNewPositionAsync(symbol, ct);
            
            if (!positionCheck.CanOpen)
            {
                _logger.LogWarning("[ENTRY_SKIPPED] {Symbol} - PositionGuard: {Reason}, Correlation={Correlation}",
                    symbol, positionCheck.Reason, correlationId);
                
                _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(5));
                EmitTickSummary(correlationId, tickStart, tickStage, false, positionCheck.BlockReason, symbol);
                return;
            }

            // ✅ PHASE 8: COOLDOWN CHECK (< 10ms)
            tickStage = "CooldownCheck";
            var cooldownCheck = await _smartCooldown.CanEnterAsync(symbol, nowUtc, ct);
            
            if (!cooldownCheck.Allowed)
            {
                _logger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Cooldown: {Reason}, Correlation={Correlation}",
                    symbol, cooldownCheck.Reason, correlationId);
                
                await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, cooldownCheck.Reason, ct);
                EmitTickSummary(correlationId, tickStart, tickStage, false, "COOLDOWN", symbol);
                return;
            }

            // ✅ PHASE 9: SPIKE CHECK (< 10ms)
            tickStage = "SpikeCheck";
            var spikeCheck = await _smartCooldown.CheckSpikeBlockAsync(symbol, nowUtc, ct);
            
            if (!spikeCheck.Allowed)
            {
                _logger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Spike: {Reason}, Correlation={Correlation}",
                    symbol, spikeCheck.Reason, correlationId);
                
                await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, spikeCheck.Reason, ct);
                EmitTickSummary(correlationId, tickStart, tickStage, false, "SPIKE_BLOCK", symbol);
                return;
            }

            // ✅ PHASE 10: ACQUIRE LOCK (< 5ms)
            tickStage = "AcquireLock";
            var lockAcquired = await _executionGuard.TryAcquireSymbolLockAsync(symbol, "ENTRY", ct);
            
            if (!lockAcquired)
            {
                _logger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Lock busy, Correlation={Correlation}",
                    symbol, correlationId);
                
                EmitTickSummary(correlationId, tickStart, tickStage, false, "LOCK_BUSY", symbol);
                return;
            }

            try
            {
                // ✅ PHASE 11: RACE CONDITION CHECKS (< 20ms)
                tickStage = "RaceChecks";
                var portfolioRecheck = await _portfolio.GetPortfolioAsync(ct);
                
                if (portfolioRecheck.FreeUsdt < effectiveCapital)
                {
                    EmitTickSummary(correlationId, tickStart, tickStage, false, "RACE_BALANCE", symbol);
                    return;
                }

                var activeRecheck = await _db.Positions.CountAsync(p => p.IsActive && p.IsOpen, ct);
                if (settingsSnapshot.MaxConcurrentPositions > 0 && 
                    activeRecheck >= settingsSnapshot.MaxConcurrentPositions)
                {
                    EmitTickSummary(correlationId, tickStart, tickStage, false, "RACE_POSITIONS", symbol);
                    return;
                }

                // ✅ PHASE 12: FINAL CONFIRMATION (< 30ms - single Binance API call)
                tickStage = "ConfirmEntry";
                var confirmResult = await _detector.ConfirmEntryNowAsync(symbol, state.Settings, ct);

                if (!confirmResult.Ok)
                {
                    _logger.LogWarning("[ENTRY_SKIPPED] {Symbol} - Confirm: {Why}, Correlation={Correlation}",
                        symbol, confirmResult.Why, correlationId);
                    
                    await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, confirmResult.Why, ct);
                    EmitTickSummary(correlationId, tickStart, tickStage, false, "CONFIRMATION_FAILED", symbol);
                    return;
                }

                _logger.LogInformation("✅ [ENTRY_SIGNAL_READY] {Symbol}, Correlation={Correlation}",
                    symbol, correlationId);

                // ✅ PHASE 13: EXECUTION (< 100ms - Binance order placement)
                var shouldExecute = settingsSnapshot.ExecuteTrades && !_coreV2ShadowMode;

                if (!shouldExecute)
                {
                    var mode = !settingsSnapshot.ExecuteTrades ? "DRY_RUN" : "SHADOW";
                    _logger.LogWarning("[{Mode}] {Symbol} - Would place order ${Size:N2}", 
                        mode, symbol, effectiveCapital);
                    
                    EmitTickSummary(correlationId, tickStart, "SimulateOnly", true, mode, symbol);
                    return;
                }

                tickStage = "Execute";
                _logger.LogInformation("🚀 [ENTRY_ATTEMPT] {Symbol}, Size=${Size:N2}, Correlation={Correlation}",
                    symbol, effectiveCapital, correlationId);

                var execResult = await _precisionExecutor.ExecutePrecisionTradeAsync(
                    symbol,
                    effectiveCapital,
                    settingsSnapshot,
                    regimeDecision,
                    (level, msg) => _logger.LogDebug("[EXECUTOR] {Level}: {Message}", level, msg),
                    ct);

                if (execResult.Success)
                {
                    _logger.LogInformation("🎉 [ENTRY_SUCCESS] {Symbol}, PositionId={Id}, Correlation={Correlation}",
                        symbol, execResult.PositionId, correlationId);
                    
                    await _smartCooldown.RecordEntryFilledAsync(symbol, nowUtc, ct);
                    EmitTickSummary(correlationId, tickStart, tickStage, true, null, symbol, execResult.PositionId);
                }
                else
                {
                    _logger.LogError("❌ [ENTRY_FAIL] {Symbol}, Error={Error}", symbol, execResult.Error);
                    await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, execResult.Error ?? "UNKNOWN", ct);
                    EmitTickSummary(correlationId, tickStart, tickStage, false, "EXECUTION_FAILED", symbol);
                }
            }
            finally
            {
                await _executionGuard.ReleaseSymbolLockAsync(symbol, "ENTRY", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[STRATEGY_EXCEPTION] Stage={Stage}, Correlation={Correlation}",
                tickStage, correlationId);
            
            EmitTickSummary(correlationId, tickStart, tickStage, false, "EXCEPTION", null);
        }
    }

    // Add this method to implement the missing interface member
    public async Task EvaluateAsync(BotMarketSnapshot market, BotState state, CancellationToken ct)
    {
        await EvaluateAsync(state, ct);
    }

    private void EmitTickSummary(
        string correlationId,
        DateTime tickStart,
        string stage,
        bool success,
        string? blockReason,
        string? symbol,
        int? positionId = null)
    {
        var duration = (DateTime.UtcNow - tickStart).TotalMilliseconds;

        _logger.LogInformation(
            "[TICK_SUMMARY] Correlation={Correlation}, Stage={Stage}, Success={Success}, " +
            "BlockReason={Reason}, Symbol={Symbol}, PositionId={PositionId}, Duration={Duration}ms",
            correlationId, stage, success, blockReason ?? "N/A", symbol ?? "N/A", positionId?.ToString() ?? "N/A", duration);
    }
}