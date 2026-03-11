using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CoinSoul.Entities;

// ✅ Entities لا تعتمد على Trading.Core (تجنب Cycle)

public sealed class BotSettingsEntity
{
    public int Id { get; set; }

    [MaxLength(10)]
    public string TradeMode { get; set; } = "Spot";

    public int StrategyModeValue { get; set; } = 4;

    public bool IsRunning { get; set; } = false;
    public DateTime? LastStartUtc { get; set; }
    public DateTime? LastStopUtc { get; set; }

    // ===== CRITICAL SAFETY FLAGS =====
    public bool ExecuteTrades { get; set; } = false; // DRY RUN by default
    public bool KillSwitch { get; set; } = false; // Emergency stop
    public decimal MaxAllowedEntrySlippagePct { get; set; } = 0.20m; // 0.20%
    public int ReconcileIntervalSeconds { get; set; } = 30;
    public int BalanceRefreshCooldownMs { get; set; } = 2000;
    public decimal DustIgnoreUsdThreshold { get; set; } = 1.0m;

    // ✅ NEW: Two-step live trading arming
    public bool? LiveArmed { get; set; }

    // ===== Core Trading Parameters =====
    public decimal TradeSizeUsd { get; set; } = 25m;
    public decimal CapitalPerTradeUsdt { get; set; } = 25m;
    public decimal TakeProfitGrossPct { get; set; } = 1.0m;
    public decimal StopLossGrossPct { get; set; } = 1.5m;
    public int MaxTradeDurationMinutes { get; set; } = 30;
    public decimal QtyBufferPct { get; set; } = 0.002m;
    public decimal OcoStopLimitBufferPct { get; set; } = 0.001m;
    public bool UniqueSymbolOnly { get; set; } = true;
    public int MaxConcurrentPositions { get; set; } = 1;
    public bool BlockSameSymbolReentry { get; set; } = true;

    // ===== Net Profit Engine =====
    public decimal MakerFeeRate { get; set; } = 0.001m;
    public decimal TakerFeeRate { get; set; } = 0.001m;
    public decimal NetProfitTargetUsd { get; set; } = 0.25m;
    public decimal SlippageBufferPct { get; set; } = 0.0005m;
    public decimal SpreadBufferPct { get; set; } = 0.0003m;

    // ===== Strategy D (Legacy) =====
    public bool AutoScalperEnabled { get; set; } = false;
    public int MaxTradeDurationSeconds { get; set; } = 240;
    public decimal HardStopLossPct { get; set; } = 0.8m;
    public decimal MaxSpreadPct { get; set; } = 0.15m;
    public decimal Min24hQuoteVolumeUsdt { get; set; } = 50_000_000m;
    public decimal SlippageBufferUsd { get; set; } = 0.02m;

    // ===== Risk Guards =====
    public decimal EquityStartOfDayUsdt { get; set; }
    public DateTime? PauseUntilUtc { get; set; }
    public DateTime? StopUntilUtc { get; set; }
    public decimal RiskGuardPause30MinPct { get; set; } = -5m;
    public decimal RiskGuardPause3HourPct { get; set; } = -10m;
    public decimal RiskGuardStopUntilMidnightPct { get; set; } = -15m;

    // ===== Legacy =====
    public decimal MaxUsdPerTrade { get; set; } = 3m;
    public bool PaperTrading { get; set; } = false;
    public int TickSeconds { get; set; } = 2;
    public int TimeExitMinutes { get; set; } = 4;
    public bool IsEnabled { get; set; } = true;
    public decimal MaxEntrySlippagePct { get; set; } = 0.30m;

    // ===== Smart Cooldown Layer =====
    public int CooldownSameSymbolSeconds { get; set; } = 180;
    public int CooldownAfterLossSeconds { get; set; } = 300;
    public int CooldownAfterEntrySeconds { get; set; } = 60;
    public int MaxEntryAttemptsPerSymbolPer15Min { get; set; } = 3;
    public int CooldownAfterTooManyAttemptsSeconds { get; set; } = 900;
    
    public decimal SpikeBlockAtrPct { get; set; } = 3.00m;
    public decimal SpikeBlock1mMovePct { get; set; } = 2.20m;
    public int SpikeCheckLookbackMinutes { get; set; } = 15;
    
    public bool EnableSmartCooldown { get; set; } = true;
    public bool EnableSpikeBlock { get; set; } = true;

    // ===== Market Regime Filter =====
    public bool EnableMarketRegimeFilter { get; set; } = true;
    [MaxLength(20)]
    public string RegimeAnchorSymbol { get; set; } = "BTCUSDT";
    [MaxLength(10)]
    public string RegimeTimeframe { get; set; } = "15m";
    public int RegimeLookbackBars { get; set; } = 220;
    public int RegimeFastEmaPeriod { get; set; } = 50;
    public int RegimeSlowEmaPeriod { get; set; } = 200;
    public int RegimeAtrPeriod { get; set; } = 14;
    public decimal SidewaysAtrPctThreshold { get; set; } = 0.80m;
    public decimal TrendAtrPctThreshold { get; set; } = 1.20m;
    public bool BlockTradingOnCrash { get; set; } = true;
    public decimal Crash1hMovePct { get; set; } = 2.50m;
    public int CrashLookbackMinutes { get; set; } = 60;
    public decimal RiskMultBull { get; set; } = 1.00m;
    public decimal RiskMultBear { get; set; } = 0.60m;
    public decimal RiskMultSideways { get; set; } = 0.70m;
    public decimal RiskMultCrash { get; set; } = 0.00m;
    public decimal TpMultBull { get; set; } = 1.00m;
    public decimal TpMultBear { get; set; } = 0.80m;
    public decimal TpMultSideways { get; set; } = 0.85m;
    public decimal TpMultCrash { get; set; } = 1.00m;
    public bool ForceConservativeInBear { get; set; } = true;

    // ===== Strategy Auto Control Center =====
    public decimal MinFreeBalanceUsdt { get; set; } = 0m;
    public decimal MinUsdtToOpenNewPosition { get; set; } = 18m;
    public bool AllowMultipleSymbols { get; set; } = true;
    public bool IncludeFeesInTP { get; set; } = true;
    public bool UseOcoExit { get; set; } = true;
    public bool PlaceSeparateTpSlIfOcoFails { get; set; } = true;
    public int EntryCooldownSeconds { get; set; } = 30;
    public int SmartCooldownMinutes { get; set; } = 15;
    public int MaxReentriesPerSymbolPerHour { get; set; } = 3;
    public int BlockRevengeTradingMinutes { get; set; } = 10;
    public int RegimeTimeframeMinutes { get; set; } = 15;
    public int BtcEmaPeriod { get; set; } = 200;
    public decimal HighVolAtrPctThreshold { get; set; } = 1.20m;
    public decimal RegimeRiskScale { get; set; } = 0.70m;
    public decimal RegimeTpScale { get; set; } = 0.85m;
    public int RegimeAtrLookback { get; set; } = 50;
    public decimal RsiMaxForEntry { get; set; } = 80m;
    public decimal MomentumMinPct { get; set; } = -0.50m;
    public bool RejectShortTermPeak { get; set; } = true;
    public decimal MinVolume24hUsd { get; set; } = 25_000m;
    public bool TradingEnabled { get; set; } = true;
    public TimeSpan? TradingStartTime { get; set; } = new TimeSpan(0, 0, 0);
    public TimeSpan? TradingEndTime { get; set; } = new TimeSpan(23, 59, 59);
    [MaxLength(1000)]
    public string? Notes { get; set; }

    // === Entry Execution Settings ===
    public decimal MinFreeUsdtReserve { get; set; } = 5m;
    public bool UseLimitMakerEntry { get; set; } = true;
    public decimal LimitMakerDiscountBps { get; set; } = 5m;
    public int LimitMakerTimeoutSeconds { get; set; } = 4;
    public bool FallbackToMarketOnEntryTimeout { get; set; } = true;
    public int OcoRetryAttempts { get; set; } = 1;

    // === Dynamic Sizing & Trade History Settings ===
    public decimal TargetUsdPerTrade { get; set; } = 18m;
    public decimal MinUsdPerTrade { get; set; } = 18m;
    public int MaxOpenTrades { get; set; } = 20;
    public bool PreventSameSymbolTwice { get; set; } = true;
    public int TradeHistoryTopSymbols { get; set; } = 400;

    // ===== Opportunity Intelligence =====
    public int QueueSize { get; set; } = 160;
    public int DeepScanTopN { get; set; } = 20;
    public decimal TierAConfidenceThreshold { get; set; } = 0.78m;
    public decimal TierBConfidenceThreshold { get; set; } = 0.62m;
    public decimal TierCConfidenceThreshold { get; set; } = 0.45m;
    public decimal ExpectedNetAfterFeesUsd { get; set; } = 0.003m;
    public int OpportunitySwitchHoldMinutes { get; set; } = 20;
    public decimal OpportunitySwitchMinConfidenceGap { get; set; } = 0.05m;
    public int SoftReviewMinutes1 { get; set; } = 5;
    public int SoftReviewMinutes2 { get; set; } = 15;
    public decimal FinalEntryMaxSpreadPct { get; set; } = 0.40m;
    public decimal FinalEntryMinOrderbookImbalance { get; set; } = 1.002m;
    public decimal FinalEntryMinMomentumPct { get; set; } = 0.0005m;
    public decimal ApiBudgetPerMinute { get; set; } = 1200m;
}

public sealed class PositionEntity
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Symbol { get; set; } = "";

    // ✅ Trade Stage
    public int Stage { get; set; } = 0; // TradeStage enum as int

    public decimal EntryPrice { get; set; }
    public decimal? ExitPrice { get; set; }

    public decimal Quantity { get; set; }
    public decimal QuoteUsedUsdt { get; set; }

    // ✅ Net Profit Tracking
    public decimal TargetNetProfitUsd { get; set; } = 0.25m;
    public decimal FeesPaidUsd { get; set; }
    public decimal? NetProfitUsd { get; set; }

    public decimal FeesUsdt { get; set; }
    public decimal NetPnlUsdt { get; set; }

    public bool IsOpen { get; set; } = true;
    public bool IsActive { get; set; } = true; // ✅ NEW

    public bool ExitRequested { get; set; } = false;
    public bool ExitCompleted { get; set; } = false;
    public int ExitAttempts { get; set; } = 0;
    public DateTime? LastExitAttemptUtc { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime OpenedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ClosedAtUtc { get; set; }

    public string? ExitReasonValue { get; set; }
    
    [MaxLength(100)]
    public string? CloseReason { get; set; } // ✅ NEW

    [MaxLength(500)]
    public string? LastError { get; set; } // ✅ NEW

    public long? BuyOrderId { get; set; }
    public long? OcoOrderId { get; set; }
    public long? SellOrderId { get; set; }
    
    public long? TakeProfitOrderId { get; set; } // ✅ NEW (fallback TP)
    public long? StopLossOrderId { get; set; } // ✅ NEW (fallback SL)
}

public sealed class TradeEntity
{
    public long Id { get; set; }

    [MaxLength(30)]
    public string Symbol { get; set; } = "";

    [MaxLength(10)]
    public string Side { get; set; } = "";

    public decimal Price { get; set; }
    public decimal Quantity { get; set; }
    public decimal QuoteQty { get; set; }

    public decimal FeeUsdt { get; set; }
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    public int? PositionId { get; set; }
}

public sealed class OrderEntity
{
    public long Id { get; set; }

    [MaxLength(30)]
    public string Symbol { get; set; } = "";

    public long BinanceOrderId { get; set; }

    [MaxLength(20)]
    public string Type { get; set; } = "";

    [MaxLength(20)]
    public string Status { get; set; } = "";

    public decimal Quantity { get; set; }
    public decimal? Price { get; set; }

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    public int? PositionId { get; set; }
}

// ✅ KEEP ONLY ONE EventEntity DEFINITION - Removed duplicate at line 189
public sealed class EventEntity
{
    public int Id { get; set; }

    public DateTime AtUtc { get; set; } = DateTime.UtcNow;

    [MaxLength(100)]
    public string Type { get; set; } = string.Empty;

    [MaxLength(50)]
    public string Level { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string Message { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Symbol { get; set; }

    public int? PositionId { get; set; }

    // ✅ For JSON metadata (correlation ID, etc.)
    public string? Data { get; set; }
}

public sealed class ExecutionLockEntity
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Symbol { get; set; } = "";

    [MaxLength(20)]
    public string LockType { get; set; } = "";

    public DateTime AcquiredAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddSeconds(30);

    [MaxLength(100)]
    public string? Reference { get; set; }
}

public sealed class ExecutionAttemptEntity
{
    public int Id { get; set; }

    [MaxLength(200)]
    public string IdempotencyKey { get; set; } = "";

    [MaxLength(30)]
    public string Symbol { get; set; } = "";

    [MaxLength(20)]
    public string AttemptType { get; set; } = "";

    public bool Success { get; set; }
    public DateTime AttemptedAtUtc { get; set; } = DateTime.UtcNow;

    public long? OrderId { get; set; }

    [MaxLength(500)]
    public string? Result { get; set; }
}

public sealed class EquitySnapshotEntity
{
    public int Id { get; set; }
    
    public DateTime AtUtc { get; set; } = DateTime.UtcNow;
    public DateTime DayUtc { get; set; }
    
    public decimal TotalEquityUsdt { get; set; }
    public decimal FreeUsdt { get; set; }
    public decimal LockedUsdt { get; set; }
    public decimal StartOfDayEquityUsdt { get; set; }
    
    [MaxLength(2000)]
    public string? TopHoldings { get; set; }

    public bool IsStartOfDay { get; set; }
}

public sealed class TradeCooldownEntity
{
    public int Id { get; set; }

    [MaxLength(30)]
    public string Symbol { get; set; } = "";

    public DateTimeOffset WindowStartUtc { get; set; } = DateTimeOffset.UtcNow;
    public int AttemptsInWindow { get; set; } = 0;
    
    public DateTimeOffset? CooldownUntilUtc { get; set; }
    public DateTimeOffset? LastEntryUtc { get; set; }
    public DateTimeOffset? LastLossUtc { get; set; }
    public DateTimeOffset? LastRejectionUtc { get; set; }
    
    [MaxLength(200)]
    public string? LastReason { get; set; }
}

public sealed class TradingEventEntity
{
    public long Id { get; set; }
    
    public DateTimeOffset AtUtc { get; set; } = DateTimeOffset.UtcNow;
    
    [MaxLength(10)]
    public string Level { get; set; } = "INFO";
    
    [MaxLength(50)]
    public string Type { get; set; } = "";
    
    [MaxLength(30)]
    public string? Symbol { get; set; }
    
    [MaxLength(1000)]
    public string Message { get; set; } = "";
    
    public decimal? Price { get; set; }
    public decimal? Quantity { get; set; }
    public decimal? FeeUsdt { get; set; }
    public decimal? RealizedPnlUsdt { get; set; }
    
    [MaxLength(100)]
    public string? CorrelationId { get; set; }
    
    public int? BotInstanceId { get; set; }
}

public sealed class AccountTradeEntity
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public long TradeId { get; set; }

    [Required]
    [MaxLength(20)]
    public string Symbol { get; set; } = "";

    [Required]
    [MaxLength(10)]
    public string Side { get; set; } = "";

    [Column(TypeName = "decimal(18,8)")]
    public decimal Price { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal Quantity { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal QuoteQty { get; set; }

    [Column(TypeName = "decimal(18,8)")]
    public decimal Commission { get; set; }

    [MaxLength(10)]
    public string CommissionAsset { get; set; } = "";

    public bool IsMaker { get; set; }

    public DateTime TradeTimeUtc { get; set; }

    [Required]
    [MaxLength(20)]
    public string Source { get; set; } = "SYNC";

    public long? OrderId { get; set; }
}
