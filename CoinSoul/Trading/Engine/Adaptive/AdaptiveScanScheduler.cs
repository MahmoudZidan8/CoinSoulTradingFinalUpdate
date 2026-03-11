using System.Collections.Concurrent;
using CoinSoul.Trading.Application;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Adaptive;

public sealed class AdaptiveScanScheduler : IScanScheduler
{
    private readonly ILogger<AdaptiveScanScheduler> _logger;
    private readonly IClock _clock;
    private readonly AdaptiveScanOptions _options;

    private readonly ConcurrentQueue<ScanResultRecord> _recentScans = new();
    private readonly ConcurrentDictionary<string, int> _categoryCounts = new();
    
    private int _totalScans = 0;
    private int _totalDelayMs = 0;

    public AdaptiveScanScheduler(
        ILogger<AdaptiveScanScheduler> logger,
        IClock clock,
        IConfiguration configuration)
    {
        _logger = logger;
        _clock = clock;
        
        _options = new AdaptiveScanOptions();
        configuration.GetSection("AdaptiveScan").Bind(_options);
    }

    public Task<ScanDelayDecision> GetNextScanDelayAsync(ScanMetrics metrics, CancellationToken ct)
    {
        var hitRate = CalculateRecentHitRate();
        var decision = ComputeDelay(metrics, hitRate);

        _totalScans++;
        _totalDelayMs += decision.DelayMs;
        _categoryCounts.AddOrUpdate(decision.Category, 1, (_, count) => count + 1);

        if (_options.EnableDebugLogs)
        {
            _logger.LogDebug(
                "[ADAPTIVE] Delay={DelayMs}ms | Reason={Reason} | " +
                "Regime={Regime}, OpenPos={Open}/{Max}, HitRate={HitRate:F1}%, Vol={Vol:F2}%, Cooldowns={CD}",
                decision.DelayMs,
                decision.Reason,
                metrics.Regime,
                metrics.OpenPositionsCount,
                metrics.MaxPositions,
                hitRate,
                metrics.VolatilityPct,
                metrics.CooldownCount);
        }

        return Task.FromResult(decision);
    }

    public void RecordScanResult(int scanned, int passed)
    {
        var record = new ScanResultRecord(
            _clock.UtcNow,
            scanned,
            passed);

        _recentScans.Enqueue(record);

        // Keep only last N records
        while (_recentScans.Count > _options.HitRateWindowSize)
        {
            _recentScans.TryDequeue(out _);
        }
    }

    public SchedulerStats GetStats()
    {
        var hitRate = CalculateRecentHitRate();
        var avgDelay = _totalScans > 0 
            ? _totalDelayMs / _totalScans 
            : 0;

        return new SchedulerStats(
            hitRate,
            _totalScans,
            avgDelay,
            new Dictionary<string, int>(_categoryCounts));
    }

    private ScanDelayDecision ComputeDelay(ScanMetrics metrics, double hitRate)
    {
        // ====================================================================
        // RULE 1: CRASH REGIME - Slow down or stop
        // ====================================================================
        if (metrics.Regime.Equals("Crash", StringComparison.OrdinalIgnoreCase))
        {
            if (_options.StopScanningInCrash)
            {
                return new ScanDelayDecision(
                    _options.CrashDelayMs,
                    "Market crash detected, minimal scanning",
                    "CRASH_STOP");
            }
            else
            {
                return new ScanDelayDecision(
                    _options.SlowDelayMs,
                    "Market crash, slow scanning",
                    "CRASH_SLOW");
            }
        }

        // ====================================================================
        // RULE 2: POSITIONS FULL - Slow down significantly
        // ====================================================================
        var positionFillRatio = metrics.MaxPositions > 0
            ? (double)metrics.OpenPositionsCount / metrics.MaxPositions
            : 0;

        if (positionFillRatio >= 0.9)
        {
            return new ScanDelayDecision(
                _options.SlowDelayMs,
                $"Positions nearly full ({metrics.OpenPositionsCount}/{metrics.MaxPositions})",
                "POSITIONS_FULL");
        }

        // ====================================================================
        // RULE 3: HIGH COOLDOWN PRESSURE - Slow down
        // ====================================================================
        var cooldownRatio = metrics.TotalSymbols > 0
            ? (double)metrics.CooldownCount / metrics.TotalSymbols
            : 0;

        if (cooldownRatio >= _options.HighCooldownThreshold)
        {
            return new ScanDelayDecision(
                _options.MediumDelayMs,
                $"High cooldown pressure ({metrics.CooldownCount}/{metrics.TotalSymbols})",
                "HIGH_COOLDOWN");
        }

        // ====================================================================
        // RULE 4: LOW HIT RATE - Slow down to avoid wasted API calls
        // ====================================================================
        if (hitRate < _options.LowHitRateThreshold && _totalScans >= _options.HitRateWindowSize)
        {
            return new ScanDelayDecision(
                _options.MediumDelayMs,
                $"Low hit rate ({hitRate:F1}%), reducing scan frequency",
                "LOW_HIT_RATE");
        }

        // ====================================================================
        // RULE 5: HIGH VOLATILITY + LOW POSITIONS - Scan fast
        // ====================================================================
        if (metrics.VolatilityPct >= _options.HighVolatilityThreshold && 
            positionFillRatio < 0.5)
        {
            return new ScanDelayDecision(
                _options.FastDelayMs,
                $"High volatility ({metrics.VolatilityPct:F2}%) with capacity, fast scanning",
                "HIGH_VOL_FAST");
        }

        // ====================================================================
        // RULE 6: BULL REGIME + GOOD HIT RATE - Scan moderately fast
        // ====================================================================
        if (metrics.Regime.Equals("Bull", StringComparison.OrdinalIgnoreCase) &&
            hitRate >= _options.GoodHitRateThreshold)
        {
            return new ScanDelayDecision(
                _options.FastDelayMs,
                $"Bull regime with good hit rate ({hitRate:F1}%)",
                "BULL_FAST");
        }

        // ====================================================================
        // RULE 7: BEAR/SIDEWAYS - Normal delay
        // ====================================================================
        if (metrics.Regime.Equals("Bear", StringComparison.OrdinalIgnoreCase) ||
            metrics.Regime.Equals("Sideways", StringComparison.OrdinalIgnoreCase))
        {
            return new ScanDelayDecision(
                _options.NormalDelayMs,
                $"{metrics.Regime} regime, normal scanning",
                "REGIME_NORMAL");
        }

        // ====================================================================
        // DEFAULT: Use configured default delay
        // ====================================================================
        return new ScanDelayDecision(
            _options.DefaultDelayMs,
            "Default scanning rate",
            "DEFAULT");
    }

    private double CalculateRecentHitRate()
    {
        var records = _recentScans.ToArray();
        
        if (records.Length == 0)
            return 0;

        var totalScanned = records.Sum(r => r.Scanned);
        var totalPassed = records.Sum(r => r.Passed);

        if (totalScanned == 0)
            return 0;

        return (double)totalPassed / totalScanned * 100;
    }

    private sealed record ScanResultRecord(
        DateTime TimestampUtc,
        int Scanned,
        int Passed);
}

public sealed class AdaptiveScanOptions
{
    public bool EnableDebugLogs { get; set; } = false;
    
    // Delay configurations (milliseconds)
    public int FastDelayMs { get; set; } = 1000;
    public int NormalDelayMs { get; set; } = 2000;
    public int MediumDelayMs { get; set; } = 4000;
    public int SlowDelayMs { get; set; } = 8000;
    public int DefaultDelayMs { get; set; } = 2000;
    public int CrashDelayMs { get; set; } = 30000;
    
    // Threshold configurations
    public decimal HighVolatilityThreshold { get; set; } = 2.0m; // 2% ATR
    public double HighCooldownThreshold { get; set; } = 0.5; // 50% of symbols on cooldown
    public double LowHitRateThreshold { get; set; } = 5.0; // 5% pass rate
    public double GoodHitRateThreshold { get; set; } = 15.0; // 15% pass rate
    
    // Hit rate calculation
    public int HitRateWindowSize { get; set; } = 10; // Last 10 scans
    
    // Crash behavior
    public bool StopScanningInCrash { get; set; } = false;
}