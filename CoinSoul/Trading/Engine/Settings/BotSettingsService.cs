using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Settings;

public sealed class BotSettingsService
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ITradingEngine _tradingEngine;
    private readonly ILogger<BotSettingsService> _logger;

    public BotSettingsService(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ITradingEngine tradingEngine,
        ILogger<BotSettingsService> logger)
    {
        _dbFactory = dbFactory;
        _tradingEngine = tradingEngine;
        _logger = logger;
    }

    /// <summary>
    /// Loads BotSettings from database (creates defaults if not exists)
    /// </summary>
    public async Task<BotSettingsEntity> LoadAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var entity = await db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);

            if (entity != null)
            {
                _logger.LogInformation(
                    "[SETTINGS_LOAD] Loaded Id={Id}, AutoScalperEnabled={AutoScalperEnabled}, ExecuteTrades={ExecuteTrades}",
                    entity.Id, entity.AutoScalperEnabled, entity.ExecuteTrades);

                return entity;
            }

            // Create defaults if not exists
            var created = CreateDefaults();
            db.BotSettings.Add(created);
            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[SETTINGS_LOAD] Created defaults - Id={Id}, AutoScalperEnabled={AutoScalperEnabled}",
                created.Id, created.AutoScalperEnabled);

            return created;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SETTINGS_LOAD_ERROR] Failed to load BotSettings");
            throw;
        }
    }

    /// <summary>
    /// Saves BotSettings using proper EF Core tracking
    /// CRITICAL: This method MUST persist ALL properties including AutoScalperEnabled
    /// </summary>
    public async Task SaveAsync(BotSettingsEntity model, CancellationToken ct)
    {
        const int maxRetries = 2;
        var attempt = 0;

        while (attempt < maxRetries)
        {
            attempt++;

            try
            {
                await using var db = await _dbFactory.CreateDbContextAsync(ct);

                // ✅ CRITICAL: Load tracked entity (not AsNoTracking)
                var existing = await db.BotSettings.FirstOrDefaultAsync(ct);

                if (existing == null)
                {
                    // Should rarely happen - insert new
                    _logger.LogWarning("[SETTINGS_SAVE] No existing entity found, inserting new");

                    // ✅ CRITICAL FIX: Ensure StrategyModeValue is 4 when AutoScalperEnabled
                    if (model.AutoScalperEnabled)
                    {
                        model.StrategyModeValue = 4; // Force to AutoScalperD
                    }

                    db.BotSettings.Add(model);
                    await db.SaveChangesAsync(ct);

                    _logger.LogInformation(
                        "[SETTINGS_SAVE] Inserted new - AutoScalperEnabled={Auto}, StrategyModeValue={Strategy}",
                        model.AutoScalperEnabled, model.StrategyModeValue);

                    await ApplyToRuntimeAsync(model, ct);
                    return;
                }

                // ✅ LOG BEFORE SAVE
                _logger.LogInformation(
                    "[SETTINGS_BEFORE_SAVE] Existing: AutoScalperEnabled={ExistingAuto}, StrategyModeValue={ExistingStrategy}, ExecuteTrades={ExistingExec}",
                    existing.AutoScalperEnabled,
                    existing.StrategyModeValue,
                    existing.ExecuteTrades);

                _logger.LogInformation(
                    "[SETTINGS_BEFORE_SAVE] Incoming: AutoScalperEnabled={IncomingAuto}, StrategyModeValue={IncomingStrategy}, ExecuteTrades={IncomingExec}",
                    model.AutoScalperEnabled,
                    model.StrategyModeValue,
                    model.ExecuteTrades);

                // ✅ CRITICAL FIX: Ensure StrategyModeValue is 4 when AutoScalperEnabled
                if (model.AutoScalperEnabled && model.StrategyModeValue != 4)
                {
                    _logger.LogWarning(
                        "[SETTINGS_FIX] AutoScalperEnabled=true but StrategyModeValue={Value}, correcting to 4",
                        model.StrategyModeValue);

                    model.StrategyModeValue = 4; // Force to AutoScalperD
                }

                // ✅ CRITICAL: Use SetValues to copy ALL properties from model to tracked entity
                db.Entry(existing).CurrentValues.SetValues(model);

                // ✅ Explicit verification that AutoScalperEnabled was set
                _logger.LogInformation(
                    "[SETTINGS_AFTER_SETVALUES] AutoScalperEnabled={CurrentAuto}, StrategyModeValue={CurrentStrategy}",
                    existing.AutoScalperEnabled,
                    existing.StrategyModeValue);

                await db.SaveChangesAsync(ct);

                // ✅ VERIFY: Re-read from DB to confirm persistence
                var verified = await db.BotSettings
                    .AsNoTracking()
                    .FirstAsync(ct);

                _logger.LogInformation(
                    "[SETTINGS_VERIFY] DB shows: AutoScalperEnabled={Auto}, StrategyModeValue={Strategy}, ExecuteTrades={Exec}, KillSwitch={Kill}, TP={TP}%, SL={SL}%",
                    verified.AutoScalperEnabled,
                    verified.StrategyModeValue,
                    verified.ExecuteTrades,
                    verified.KillSwitch,
                    verified.TakeProfitGrossPct,
                    verified.StopLossGrossPct);

                // Apply to runtime engine
                await ApplyToRuntimeAsync(verified, ct);

                _logger.LogInformation("[SETTINGS_SAVE] ✅ Save completed successfully");
                return;
            }
            catch (DbUpdateConcurrencyException ex)
            {
                _logger.LogWarning(ex, "[SETTINGS_SAVE] Concurrency conflict, retry {Attempt}/{Max}",
                    attempt, maxRetries);

                if (attempt >= maxRetries)
                {
                    _logger.LogError("[SETTINGS_SAVE_ERROR] Concurrency retries exhausted");
                    throw;
                }

                // Wait before retry
                await Task.Delay(100 * attempt, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[SETTINGS_SAVE_ERROR] Failed to save BotSettings on attempt {Attempt}",
                    attempt);
                throw;
            }
        }
    }

    /// <summary>
    /// Applies settings to runtime trading engine
    /// </summary>
    private async Task ApplyToRuntimeAsync(BotSettingsEntity entity, CancellationToken ct)
    {
        try
        {
            var coreSettings = entity.ToCoreSettings();
            await _tradingEngine.EnqueueAsync(new UpdateBotSettingsCommand(coreSettings), ct);

            _logger.LogInformation(
                "[SETTINGS_RUNTIME] Applied to engine - AutoScalperEnabled={Auto}",
                entity.AutoScalperEnabled);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SETTINGS_RUNTIME_ERROR] Failed to apply to runtime");
            // Don't throw - DB save succeeded, runtime update is secondary
        }
    }

    /// <summary>
    /// Resets all settings to defaults
    /// </summary>
    public async Task<BotSettingsEntity> ResetToDefaultsAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            var existing = await db.BotSettings.FirstOrDefaultAsync(ct);

            if (existing == null)
            {
                existing = new BotSettingsEntity();
                db.BotSettings.Add(existing);
            }

            var defaults = CreateDefaults();

            // ✅ Use SetValues for full property copy
            db.Entry(existing).CurrentValues.SetValues(defaults);

            await db.SaveChangesAsync(ct);

            _logger.LogInformation(
                "[SETTINGS_RESET] Reset to defaults - AutoScalperEnabled={Auto}",
                defaults.AutoScalperEnabled);

            await ApplyToRuntimeAsync(existing, ct);

            return await db.BotSettings.AsNoTracking().FirstAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SETTINGS_RESET_ERROR] Failed to reset BotSettings");
            throw;
        }
    }

    /// <summary>
    /// Creates default settings entity
    /// </summary>
    private static BotSettingsEntity CreateDefaults()
    {
        return new BotSettingsEntity
        {
            TradeMode = "Spot",
            StrategyModeValue = (int)StrategyMode.AutoScalperD,
            IsEnabled = true,
            AutoScalperEnabled = true, // ✅ CRITICAL - Default to true
            PaperTrading = false,

            // ===== CRITICAL SAFETY FLAGS =====
            ExecuteTrades = false, // DRY RUN by default
            KillSwitch = false,
            MaxAllowedEntrySlippagePct = 0.20m,
            ReconcileIntervalSeconds = 30,
            BalanceRefreshCooldownMs = 2000,
            DustIgnoreUsdThreshold = 1.0m,

            // Capital
            TradeSizeUsd = 25m,
            CapitalPerTradeUsdt = 25m,
            MinFreeBalanceUsdt = 0m,
            MinUsdtToOpenNewPosition = 18m,
            AllowMultipleSymbols = true,
            // ✅ Requested default
            MaxConcurrentPositions = 10,
            BlockSameSymbolReentry = true,

            // Dynamic Sizing
            TargetUsdPerTrade = 18m,
            MinUsdPerTrade = 18m,
            // ✅ Requested default
            MaxOpenTrades = 20,
            PreventSameSymbolTwice = true,
            TradeHistoryTopSymbols = 300,
            MinFreeUsdtReserve = 5m,

            // Targets
            TakeProfitGrossPct = 1.0m,
            StopLossGrossPct = 1.5m,
            NetProfitTargetUsd = 0.25m,
            IncludeFeesInTP = true,

            // Fees
            MakerFeeRate = 0.0010m,
            TakerFeeRate = 0.0010m,
            SlippageBufferPct = 0.0002m,
            SpreadBufferPct = 0.0002m,
            SlippageBufferUsd = 0.05m,

            // Execution
            UseOcoExit = true,
            OcoStopLimitBufferPct = 0.10m,
            PlaceSeparateTpSlIfOcoFails = true,
            EntryCooldownSeconds = 30,
            UseLimitMakerEntry = true,
            LimitMakerDiscountBps = 5m,
            LimitMakerTimeoutSeconds = 3,
            FallbackToMarketOnEntryTimeout = true,
            OcoRetryAttempts = 2,

            // Cooldown
            EnableSmartCooldown = true,
            SmartCooldownMinutes = 15,
            MaxReentriesPerSymbolPerHour = 3,
            BlockRevengeTradingMinutes = 10,
            EnableSpikeBlock = true,
            SpikeBlockAtrPct = 2.40m,
            SpikeBlock1mMovePct = 1.80m,
            CooldownAfterEntrySeconds = 2,
            CooldownAfterLossSeconds = 15,
            CooldownSameSymbolSeconds = 20,

            // Regime
            EnableMarketRegimeFilter = true,
            RegimeTimeframeMinutes = 15,
            BtcEmaPeriod = 200,
            SidewaysAtrPctThreshold = 0.80m,
            HighVolAtrPctThreshold = 1.20m,
            RegimeRiskScale = 0.70m,
            RegimeTpScale = 0.85m,
            RegimeAtrLookback = 50,
            RegimeFastEmaPeriod = 12,
            RegimeSlowEmaPeriod = 26,

            // Signals
            RsiMaxForEntry = 80m,
            MomentumMinPct = -0.35m,
            RejectShortTermPeak = true,
            MinVolume24hUsd = 50_000m,
            MaxSpreadPct = 0.60m,

            // Session
            TradingEnabled = true,
            TradingStartTime = new TimeSpan(0, 0, 0),
            TradingEndTime = new TimeSpan(23, 59, 59),

            // Timing
            TickSeconds = 2,
            MaxTradeDurationSeconds = 240,
            MaxTradeDurationMinutes = 4,
            TimeExitMinutes = 4,
            QtyBufferPct = 0.5m,

            // High-frequency scalp v19
            QueueSize = 120,
            DeepScanTopN = 20,
            TierAConfidenceThreshold = 0.82m,
            TierBConfidenceThreshold = 0.68m,
            TierCConfidenceThreshold = 0.52m,
            ExpectedNetAfterFeesUsd = 0.004m,
            FinalEntryMaxSpreadPct = 0.35m,
            FinalEntryMinOrderbookImbalance = 1.005m,
            FinalEntryMinMomentumPct = 0.001m,
            ApiBudgetPerMinute = 900m
        };
    }
}