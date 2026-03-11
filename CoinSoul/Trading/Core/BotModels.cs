namespace CoinSoul.Trading.Core;

public enum BotStatus { Stopped = 0, Running = 1, Error = 2 }
public enum TradeMode { Spot = 1, Futures = 2 }

public enum StrategyMode
{
    None = 0,
    ManualA = 1,
    SmartFilterB = 2,
    AutoSelectC = 3,
    AutoScalperD = 4
}

public enum StrategyAMode
{
    Conservative = 0,
    Balanced = 1,
    Aggressive = 2,
    Scalping = 3
}

/// <summary>
/// Runtime trading settings model - MUST mirror BotSettingsEntity for all strategy-critical fields
/// </summary>
public sealed class BotSettings
{
    // ===== Core Mode =====
    public TradeMode TradeMode { get; set; } = TradeMode.Spot;
    public StrategyMode StrategyMode { get; set; } = StrategyMode.AutoScalperD;
    public bool AutoScalperEnabled { get; set; } = true;
    public bool PaperTrading { get; set; } = false;

    // ===== CRITICAL SAFETY FLAGS =====
    public bool ExecuteTrades { get; set; } = false; // DRY RUN by default
    public bool KillSwitch { get; set; } = false;
    public bool IsEnabled { get; set; } = true;
    public bool TradingEnabled { get; set; } = true;

    // ===== Capital & Sizing =====
    public decimal TargetUsdPerTrade { get; set; } = 18m;
    public decimal MinUsdPerTrade { get; set; } = 18m;
    public decimal TradeSizeUsd { get; set; } = 18m;
    public decimal CapitalPerTradeUsdt { get; set; } = 18m;
    public decimal MinFreeBalanceUsdt { get; set; } = 0m;
    public decimal MinFreeUsdtReserve { get; set; } = 5m;
    public decimal MinUsdtToOpenNewPosition { get; set; } = 18m;
    
    public int MaxOpenTrades { get; set; } = 20;
    public int MaxConcurrentPositions { get; set; } = 20;
    public bool PreventSameSymbolTwice { get; set; } = true;
    public bool AllowMultipleSymbols { get; set; } = true;
    public bool BlockSameSymbolReentry { get; set; } = true;
    public int TradeHistoryTopSymbols { get; set; } = 100;

    // ===== Targets & Exits =====
    public decimal TakeProfitGrossPct { get; set; } = 1.0m;
    public decimal StopLossGrossPct { get; set; } = 1.5m;
    public decimal NetProfitTargetUsd { get; set; } = 0.25m;
    public bool IncludeFeesInTP { get; set; } = true;
    
    public decimal NetProfitTargetUsdt
    {
        get => NetProfitTargetUsd;
        set => NetProfitTargetUsd = value;
    }

    // ===== Timing =====
    public int MaxTradeDurationSeconds { get; set; } = 240;
    public int MaxTradeDurationMinutes { get; set; } = 4;
    public int TimeExitMinutes { get; set; } = 4;
    public int TickSeconds { get; set; } = 2;
    
    public int TimeExitSeconds
    {
        get => MaxTradeDurationSeconds;
        set => MaxTradeDurationSeconds = value;
    }

    // ===== Risk & Safety =====
    public decimal HardStopLossPct { get; set; } = 0.8m;
    public decimal DailyLossLimitUsd { get; set; } = 30m;
    public decimal MaxAllowedEntrySlippagePct { get; set; } = 0.20m;
    public decimal MaxEntrySlippagePct { get; set; } = 0.30m;
    
    public DateTime? PauseUntilUtc { get; set; }
    public DateTime? StopUntilUtc { get; set; }
    
    public decimal RiskGuardPause30MinPct { get; set; } = -5m;
    public decimal RiskGuardPause3HourPct { get; set; } = -10m;
    public decimal RiskGuardStopUntilMidnightPct { get; set; } = -15m;

    // ===== Spread & Slippage =====
    public decimal MaxSpreadPct { get; set; } = 0.15m;
    public decimal SpreadBufferPct { get; set; } = 0.0003m;
    public decimal SlippageBufferPct { get; set; } = 0.0005m;
    public decimal SlippageBufferUsd { get; set; } = 0.02m;

    // ===== Fees =====
    public decimal MakerFeeRate { get; set; } = 0.0010m;
    public decimal TakerFeeRate { get; set; } = 0.0010m;

    // ===== Volume & Liquidity =====
    public decimal Min24hQuoteVolumeUsdt { get; set; } = 50_000_000m;
    public decimal MinVolume24hUsd { get; set; } = 100_000m;

    // ===== Execution =====
    public bool UseOcoExit { get; set; } = true;
    public decimal OcoStopLimitBufferPct { get; set; } = 0.10m;
    public bool PlaceSeparateTpSlIfOcoFails { get; set; } = true;
    public int OcoRetryAttempts { get; set; } = 1;
    
    public bool UseLimitMakerEntry { get; set; } = true;
    public decimal LimitMakerDiscountBps { get; set; } = 5m;
    public int LimitMakerTimeoutSeconds { get; set; } = 20;
    public bool FallbackToMarketOnEntryTimeout { get; set; } = true;
    
    public decimal QtyBufferPct { get; set; } = 0.002m;
    public decimal DustIgnoreUsdThreshold { get; set; } = 1.0m;

    // ===== Cooldowns =====
    public int EntryCooldownSeconds { get; set; } = 30;
    public int CooldownAfterEntrySeconds { get; set; } = 60;
    public int CooldownAfterLossSeconds { get; set; } = 300;
    public int CooldownSameSymbolSeconds { get; set; } = 180;
    
    public bool EnableSmartCooldown { get; set; } = true;
    public int SmartCooldownMinutes { get; set; } = 10;
    public int MaxReentriesPerSymbolPerHour { get; set; } = 3;
    public int BlockRevengeTradingMinutes { get; set; } = 10;
    
    public bool EnableSpikeBlock { get; set; } = true;
    public decimal SpikeBlockAtrPct { get; set; } = 3.00m;
    public decimal SpikeBlock1mMovePct { get; set; } = 2.20m;
    public int SpikeCheckLookbackMinutes { get; set; } = 15;

    // ===== Market Regime =====
    public bool EnableMarketRegimeFilter { get; set; } = true;
    public string RegimeAnchorSymbol { get; set; } = "BTCUSDT";
    public string RegimeTimeframe { get; set; } = "15m";
    public int RegimeTimeframeMinutes { get; set; } = 15;
    public int RegimeLookbackBars { get; set; } = 220;
    public int RegimeFastEmaPeriod { get; set; } = 50;
    public int RegimeSlowEmaPeriod { get; set; } = 200;
    public int RegimeAtrPeriod { get; set; } = 14;
    public int RegimeAtrLookback { get; set; } = 50;
    public int BtcEmaPeriod { get; set; } = 200;
    
    public decimal SidewaysAtrPctThreshold { get; set; } = 0.80m;
    public decimal HighVolAtrPctThreshold { get; set; } = 1.20m;
    public decimal TrendAtrPctThreshold { get; set; } = 1.20m;
    public decimal RegimeRiskScale { get; set; } = 0.70m;
    public decimal RegimeTpScale { get; set; } = 0.85m;
    
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

    // ===== Signal Filters =====
    public decimal RsiMaxForEntry { get; set; } = 80m;
    public decimal MomentumMinPct { get; set; } = -0.50m;
    public bool RejectShortTermPeak { get; set; } = true;

    // ===== Session Time =====
    public TimeSpan? TradingStartTime { get; set; } = new TimeSpan(0, 0, 0);
    public TimeSpan? TradingEndTime { get; set; } = new TimeSpan(23, 59, 59);

    // ===== Reconciliation =====
    public int ReconcileIntervalSeconds { get; set; } = 30;
    public int BalanceRefreshCooldownMs { get; set; } = 2000;

    // ===== Legacy (for backward compatibility) =====
    public List<ManualSymbolConfig> ManualSymbols { get; set; } = new();
    public int TopSymbolsCount { get; set; } = 100;
    public int LimitMakerSeconds { get; set; } = 3;
    public int SymbolCooldownMinutes { get; set; } = 5;
    public StrategyAMode StrategyAMode { get; set; } = StrategyAMode.Balanced;
    public int MaxEntryAttemptsPerSymbolPer15Min { get; set; } = 3;
    public int CooldownAfterTooManyAttemptsSeconds { get; set; } = 900;
    public bool UniqueSymbolOnly { get; set; } = true;
    public decimal MaxUsdPerTrade { get; set; } = 50m; // Legacy - use TargetUsdPerTrade instead

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
    public decimal ApiBudgetPerMinute { get; set; } = 720m;
}


public sealed class BotMarketSnapshot
{
    public Dictionary<string, decimal> Prices { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class BotLogEntry
{
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;
    public string Level { get; init; } = "INFO";
    public string Message { get; init; } = "";
}

public sealed class BotState
{
    private readonly LinkedList<BotLogEntry> _logs = new();
    private readonly object _logLock = new();

    public BotStatus Status { get; private set; } = BotStatus.Stopped;
    public string? LastError { get; private set; }
    public DateTimeOffset? LastTickAt { get; private set; }

    public BotSettings Settings { get; private set; } = new();
    public BotMarketSnapshot Market { get; private set; } = new();

    public DateTime StartedAt { get; private set; }
    public int TickCount { get; private set; }

    public List<PaperPosition> OpenPositions { get; } = new();
    public List<TradeHistoryItem> TradeHistory { get; } = new();
    public TradeStatistics Stats { get; } = new();

    public decimal DailyLossUsd
    {
        get
        {
            var today = DateTime.UtcNow.Date;
            return TradeHistory
                .Where(t => t.ClosedAtUtc.Date == today && t.PnL < 0)
                .Sum(t => -t.PnL);
        }
    }

    public TimeSpan Uptime => Status == BotStatus.Running ? (DateTime.UtcNow - StartedAt) : TimeSpan.Zero;

    private readonly Dictionary<string, DateTime> _cooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);

    public bool IsSymbolInCooldown(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        lock (_cooldownUntilUtc)
        {
            return _cooldownUntilUtc.TryGetValue(symbol, out var until) && until > DateTime.UtcNow;
        }
    }

    public void PutSymbolCooldown(string symbol, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        lock (_cooldownUntilUtc)
        {
            _cooldownUntilUtc[symbol] = DateTime.UtcNow.Add(duration);
        }
    }

    public void RegisterTrade(TradeHistoryItem item)
    {
        TradeHistory.Add(item);
        Stats.Register(item.PnL);
    }

    public IReadOnlyList<BotLogEntry> GetLogs(int takeLast = 200)
    {
        lock (_logLock)
            return _logs.TakeLast(Math.Max(1, takeLast)).ToList();
    }

    public void AddLog(string level, string message, int max = 300)
    {
        lock (_logLock)
        {
            _logs.AddLast(new BotLogEntry
            {
                Level = level,
                Message = message,
                At = DateTimeOffset.UtcNow
            });

            while (_logs.Count > max)
                _logs.RemoveFirst();
        }
    }

    public void MarkRunning()
    {
        Status = BotStatus.Running;
        StartedAt = DateTime.UtcNow;
        TickCount = 0;
        LastError = null;
        AddLog("INFO", "Bot started.");
    }

    public void MarkTick()
    {
        LastTickAt = DateTimeOffset.UtcNow;
        TickCount++;
    }

    public void MarkStopped()
    {
        Status = BotStatus.Stopped;
        AddLog("WARN", "Bot stopped.");
    }

    public void MarkError(string error)
    {
        Status = BotStatus.Error;
        LastError = error;
        AddLog("ERROR", error);
    }

    public void ApplySettings(BotSettings settings)
    {
        Settings = settings;
        AddLog("INFO", $"Settings updated: Mode={settings.TradeMode}, Strategy={settings.StrategyMode}, Paper={settings.PaperTrading}");
    }

    public void ApplyMarketSnapshot(BotMarketSnapshot snapshot)
        => Market = snapshot;
}
