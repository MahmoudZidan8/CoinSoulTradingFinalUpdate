using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.V2;

public sealed class AutoScalperTickPipeline : ITickPipeline
{
    private readonly CoinSoulDbContext _db;
    private readonly AutoScalperPositionManager _positionManager;
    private readonly SymbolQueueManager _queue;
    private readonly OpportunityDetector _detector;
    private readonly RiskGuardService _riskGuard;
    private readonly PositionGuardService _positionGuard;
    private readonly SmartCooldownService _smartCooldown;
    private readonly MarketRegimeService _regimeService;
    private readonly IPortfolioService _portfolio;
    private readonly PortfolioRefreshService _portfolioRefresh;
    private readonly PrecisionTradeExecutor _precisionExecutor;
    private readonly ExecutionGuardService _executionGuard;
    private readonly GuardEngine _guardEngine;
    private readonly ILogger<AutoScalperTickPipeline> _logger;

    private MarketRegimeDecision? _lastRegimeDecision;
    private DateTime _lastRegimeLogUtc = DateTime.MinValue;

    public AutoScalperTickPipeline(
        CoinSoulDbContext db,
        AutoScalperPositionManager positionManager,
        SymbolQueueManager queue,
        OpportunityDetector detector,
        RiskGuardService riskGuard,
        PositionGuardService positionGuard,
        SmartCooldownService smartCooldown,
        MarketRegimeService regimeService,
        IPortfolioService portfolio,
        PortfolioRefreshService portfolioRefresh,
        PrecisionTradeExecutor precisionExecutor,
        ExecutionGuardService executionGuard,
        GuardEngine guardEngine,
        ILogger<AutoScalperTickPipeline> logger)
    {
        _db = db;
        _positionManager = positionManager;
        _queue = queue;
        _detector = detector;
        _riskGuard = riskGuard;
        _positionGuard = positionGuard;
        _smartCooldown = smartCooldown;
        _regimeService = regimeService;
        _portfolio = portfolio;
        _portfolioRefresh = portfolioRefresh;
        _precisionExecutor = precisionExecutor;
        _executionGuard = executionGuard;
        _guardEngine = guardEngine;
        _logger = logger;
    }

    public async Task<TickResult> ExecuteTickAsync(BotState state, CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var tickStart = DateTime.UtcNow;
        
        _logger.LogInformation("[V2_TICK_START] CorrelationId={Correlation}", correlationId);

        try
        {
            // STAGE 1: Load Settings (single source of truth)
            var settings = await _db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            if (settings == null)
            {
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "LoadSettings",
                    Success = false,
                    BlockReason = "SETTINGS_MISSING"
                };
            }

            // STAGE 2: Validate Guards
            var portfolio = await _portfolio.GetPortfolioAsync(ct);
            var openCount = await _db.Positions.CountAsync(p => p.IsOpen, ct);
            
            var guardResult = _guardEngine.CheckPreScan(settings, state, openCount, portfolio.FreeUsdt);
            
            if (!guardResult.Allowed)
            {
                _logger.LogWarning("[V2_GUARD_BLOCK] Code={Code}, Message={Message}, Correlation={Correlation}",
                    guardResult.Code, guardResult.Message, correlationId);
                
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "ValidateGuards",
                    Success = false,
                    BlockReason = guardResult.Code,
                    Metrics = new Dictionary<string, object>
                    {
                        ["guardCode"] = guardResult.Code,
                        ["guardMessage"] = guardResult.Message,
                        ["guardDetails"] = guardResult.Details ?? new { }
                    }
                };
            }

            // STAGE 3: Manage Positions
            await _positionManager.ManageAsync(state, ct);

            // STAGE 4: Market Regime (log-on-change)
            var nowUtc = DateTimeOffset.UtcNow;
            var regimeDecision = await _regimeService.GetDecisionAsync(nowUtc, ct);
            
            var regimeChanged = _lastRegimeDecision == null ||
                               _lastRegimeDecision.Regime != regimeDecision.Regime ||
                               _lastRegimeLogUtc.AddMinutes(5) < DateTime.UtcNow;

            if (regimeChanged)
            {
                _logger.LogInformation(
                    "[V2_REGIME_CHANGE] Regime={Regime}, AllowTrade={Allow}, Risk={Risk:F2}, TP={TP:F2}, Correlation={Correlation}",
                    regimeDecision.Regime,
                    regimeDecision.AllowedToTrade,
                    regimeDecision.RiskMultiplier,
                    regimeDecision.TpMultiplier,
                    correlationId);
                
                _lastRegimeDecision = regimeDecision;
                _lastRegimeLogUtc = DateTime.UtcNow;
            }

            if (!regimeDecision.AllowedToTrade)
            {
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "MarketRegime",
                    Success = false,
                    BlockReason = $"REGIME_{regimeDecision.Regime}",
                    Metrics = new Dictionary<string, object>
                    {
                        ["regime"] = regimeDecision.Regime.ToString(),
                        ["reason"] = regimeDecision.Reason
                    }
                };
            }

            // STAGE 5: Risk Guard
            var riskCheck = await _riskGuard.CanEnterNewTradeAsync(ct);
            if (!riskCheck.CanEnter)
            {
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "RiskGuard",
                    Success = false,
                    BlockReason = "RISK_GUARD",
                    Metrics = new Dictionary<string, object> { ["reason"] = riskCheck.Reason }
                };
            }

            // STAGE 6: Capital Calculation
            var baseCapital = settings.TargetUsdPerTrade > 0 
                ? settings.TargetUsdPerTrade 
                : settings.CapitalPerTradeUsdt > 0 
                    ? settings.CapitalPerTradeUsdt 
                    : 25m;

            var effectiveCapital = Math.Max(baseCapital, baseCapital * regimeDecision.RiskMultiplier);

            if (effectiveCapital < 10m)
            {
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "CapitalCalc",
                    Success = false,
                    BlockReason = "CAPITAL_TOO_SMALL",
                    Metrics = new Dictionary<string, object> { ["effectiveCapital"] = effectiveCapital }
                };
            }

            var capitalCheck = await _portfolioRefresh.CheckCapitalAvailabilityAsync(
                settings.TargetUsdPerTrade, settings, ct);

            if (!capitalCheck.Allowed)
            {
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "CapitalAvailability",
                    Success = false,
                    BlockReason = "CAPITAL_CHECK_FAILED",
                    Metrics = new Dictionary<string, object> { ["reason"] = capitalCheck.Reason }
                };
            }

            // STAGE 7: Dequeue Symbol (triggers refresh)
            _logger.LogInformation("[V2_SCAN_START] Calling DequeueAsync, Correlation={Correlation}", correlationId);

            var q = await _queue.DequeueAsync(
                state.Settings,
                msg => _logger.LogDebug("[V2_QUEUE] {Message}, Correlation={Correlation}", msg, correlationId),
                ct);

            if (q is null || string.IsNullOrWhiteSpace(q.Symbol))
            {
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "DequeueSymbol",
                    Success = false,
                    BlockReason = "QUEUE_EMPTY"
                };
            }

            var symbol = q.Symbol;
            
            _logger.LogInformation("[V2_EVALUATING_SYMBOL] {Symbol}, Score={Score:F1}, Correlation={Correlation}",
                symbol, q.Score, correlationId);

            // STAGE 8: Position Guard
            var positionCheck = await _positionGuard.CanOpenNewPositionAsync(symbol, ct);
            if (!positionCheck.CanOpen)
            {
                _logger.LogWarning("[V2_ENTRY_SKIPPED] {Symbol} - PositionGuard: {Reason}, Correlation={Correlation}",
                    symbol, positionCheck.Reason, correlationId);
                
                _queue.MarkCooldown(symbol, TimeSpan.FromMinutes(5));
                
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "PositionGuard",
                    Success = false,
                    Symbol = symbol,
                    BlockReason = positionCheck.BlockReason,
                    Metrics = new Dictionary<string, object> { ["reason"] = positionCheck.Reason }
                };
            }

            // STAGE 9: Cooldown Check
            var cooldownCheck = await _smartCooldown.CanEnterAsync(symbol, nowUtc, ct);
            if (!cooldownCheck.Allowed)
            {
                _logger.LogWarning("[V2_ENTRY_SKIPPED] {Symbol} - Cooldown: {Reason}, Correlation={Correlation}",
                    symbol, cooldownCheck.Reason, correlationId);
                
                await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, cooldownCheck.Reason, ct);
                
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "CooldownCheck",
                    Success = false,
                    Symbol = symbol,
                    BlockReason = "COOLDOWN",
                    Metrics = new Dictionary<string, object> { ["reason"] = cooldownCheck.Reason }
                };
            }

            // STAGE 10: Spike Check
            var spikeCheck = await _smartCooldown.CheckSpikeBlockAsync(symbol, nowUtc, ct);
            if (!spikeCheck.Allowed)
            {
                _logger.LogWarning("[V2_ENTRY_SKIPPED] {Symbol} - Spike: {Reason}, Correlation={Correlation}",
                    symbol, spikeCheck.Reason, correlationId);
                
                await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, spikeCheck.Reason, ct);
                
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "SpikeCheck",
                    Success = false,
                    Symbol = symbol,
                    BlockReason = "SPIKE_BLOCK",
                    Metrics = new Dictionary<string, object> { ["reason"] = spikeCheck.Reason }
                };
            }

            // STAGE 11: Acquire Lock
            var lockAcquired = await _executionGuard.TryAcquireSymbolLockAsync(symbol, "ENTRY", ct);
            if (!lockAcquired)
            {
                _logger.LogWarning("[V2_ENTRY_SKIPPED] {Symbol} - Lock busy, Correlation={Correlation}",
                    symbol, correlationId);
                
                return new TickResult
                {
                    CorrelationId = correlationId,
                    TickStartUtc = tickStart,
                    TickEndUtc = DateTime.UtcNow,
                    Stage = "AcquireLock",
                    Success = false,
                    Symbol = symbol,
                    BlockReason = "LOCK_BUSY"
                };
            }

            try
            {
                // STAGE 12: Final Confirmation
                var confirmResult = await _detector.ConfirmEntryNowAsync(symbol, state.Settings, ct);
                if (!confirmResult.Ok)
                {
                    _logger.LogWarning("[V2_ENTRY_SKIPPED] {Symbol} - Confirmation: {Why}, Correlation={Correlation}",
                        symbol, confirmResult.Why, correlationId);
                    
                    await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, confirmResult.Why, ct);
                    
                    return new TickResult
                    {
                        CorrelationId = correlationId,
                        TickStartUtc = tickStart,
                        TickEndUtc = DateTime.UtcNow,
                        Stage = "ConfirmEntry",
                        Success = false,
                        Symbol = symbol,
                        BlockReason = "CONFIRMATION_FAILED",
                        Metrics = new Dictionary<string, object> { ["reason"] = confirmResult.Why }
                    };
                }

                _logger.LogInformation("[V2_ENTRY_SIGNAL_READY] {Symbol}, Correlation={Correlation}",
                    symbol, correlationId);

                // STAGE 13: Execute or Simulate
                var shadowMode = !settings.ExecuteTrades; // Use DB flag
                
                if (shadowMode)
                {
                    _logger.LogWarning("[V2_SHADOW_MODE] {Symbol} - Would place order ${Size:N2}, Correlation={Correlation}",
                        symbol, effectiveCapital, correlationId);
                    
                    return new TickResult
                    {
                        CorrelationId = correlationId,
                        TickStartUtc = tickStart,
                        TickEndUtc = DateTime.UtcNow,
                        Stage = "ShadowMode",
                        Success = true,
                        Symbol = symbol,
                        BlockReason = null,
                        Metrics = new Dictionary<string, object>
                        {
                            ["shadowMode"] = true,
                            ["wouldExecute"] = true,
                            ["capital"] = effectiveCapital
                        }
                    };
                }

                _logger.LogInformation("[V2_ENTRY_ATTEMPT] {Symbol}, Size=${Size:N2}, Correlation={Correlation}",
                    symbol, effectiveCapital, correlationId);

                var execResult = await _precisionExecutor.ExecutePrecisionTradeAsync(
                    symbol,
                    effectiveCapital,
                    settings,
                    regimeDecision,
                    (level, msg) => _logger.LogDebug("[V2_EXECUTOR] {Level}: {Message}", level, msg),
                    ct);

                if (execResult.Success)
                {
                    _logger.LogInformation("[V2_ENTRY_SUCCESS] {Symbol}, PositionId={Id}, Correlation={Correlation}",
                        symbol, execResult.PositionId, correlationId);
                    
                    await _smartCooldown.RecordEntryFilledAsync(symbol, nowUtc, ct);
                    
                    return new TickResult
                    {
                        CorrelationId = correlationId,
                        TickStartUtc = tickStart,
                        TickEndUtc = DateTime.UtcNow,
                        Stage = "Execute",
                        Success = true,
                        Symbol = symbol,
                        PositionId = execResult.PositionId,
                        Metrics = new Dictionary<string, object>
                        {
                            ["positionId"] = execResult.PositionId ,
                            ["capital"] = effectiveCapital
                        }
                    };
                }
                else
                {
                    _logger.LogError("[V2_ENTRY_FAIL] {Symbol}, Error={Error}, Correlation={Correlation}",
                        symbol, execResult.Error, correlationId);
                    
                    await _smartCooldown.RecordEntryAttemptAsync(symbol, nowUtc, execResult.Error ?? "UNKNOWN", ct);
                    
                    return new TickResult
                    {
                        CorrelationId = correlationId,
                        TickStartUtc = tickStart,
                        TickEndUtc = DateTime.UtcNow,
                        Stage = "Execute",
                        Success = false,
                        Symbol = symbol,
                        BlockReason = "EXECUTION_FAILED",
                        Metrics = new Dictionary<string, object> { ["error"] = execResult.Error ?? "UNKNOWN" }
                    };
                }
            }
            finally
            {
                await _executionGuard.ReleaseSymbolLockAsync(symbol, "ENTRY", ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[V2_TICK_ERROR] Correlation={Correlation}", correlationId);
            
            return new TickResult
            {
                CorrelationId = correlationId,
                TickStartUtc = tickStart,
                TickEndUtc = DateTime.UtcNow,
                Stage = "Exception",
                Success = false,
                BlockReason = "EXCEPTION",
                Metrics = new Dictionary<string, object> { ["error"] = ex.Message }
            };
        }
    }
}