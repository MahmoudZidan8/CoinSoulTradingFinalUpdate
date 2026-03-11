namespace CoinSoul.Trading.Engine.Adaptive;

/// <summary>
/// Adaptive scan scheduler that adjusts scanning frequency based on market conditions
/// </summary>
public interface IScanScheduler
{
    /// <summary>
    /// Calculate next scan delay based on current market state and bot metrics
    /// </summary>
    Task<ScanDelayDecision> GetNextScanDelayAsync(ScanMetrics metrics, CancellationToken ct);

    /// <summary>
    /// Record scan result to update hit rate statistics
    /// </summary>
    void RecordScanResult(int scanned, int passed);

    /// <summary>
    /// Get current scheduler statistics
    /// </summary>
    SchedulerStats GetStats();
}

public sealed record ScanMetrics(
    string Regime,
    int OpenPositionsCount,
    int MaxPositions,
    decimal VolatilityPct,
    int CooldownCount,
    int TotalSymbols);

public sealed record ScanDelayDecision(
    int DelayMs,
    string Reason,
    string Category);

public sealed record SchedulerStats(
    double RecentHitRatePercent,
    int TotalScans,
    int AverageDelayMs,
    Dictionary<string, int> CategoryCounts);