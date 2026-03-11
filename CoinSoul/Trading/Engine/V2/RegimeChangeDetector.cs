using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.V2;

public sealed class RegimeChangeDetector
{
    private RegimeSnapshot? _lastSnapshot;
    private DateTime _lastLoggedAtUtc = DateTime.MinValue;
    private readonly ILogger<RegimeChangeDetector> _logger;

    public RegimeChangeDetector(ILogger<RegimeChangeDetector> logger)
    {
        _logger = logger;
    }

    public bool ShouldLog(RegimeSnapshot current, int logIntervalMinutes = 5)
    {
        if (_lastSnapshot == null)
        {
            _lastSnapshot = current;
            _lastLoggedAtUtc = DateTime.UtcNow;
            return true;
        }

        if (current.HasChanged(_lastSnapshot))
        {
            _logger.LogInformation(
                "[REGIME_CHANGE] {OldRegime} -> {NewRegime}, RiskMult {OldRisk:F2} -> {NewRisk:F2}",
                _lastSnapshot.Regime,
                current.Regime,
                _lastSnapshot.RiskMultiplier,
                current.RiskMultiplier);
            
            _lastSnapshot = current;
            _lastLoggedAtUtc = DateTime.UtcNow;
            return true;
        }

        if (_lastLoggedAtUtc.AddMinutes(logIntervalMinutes) < DateTime.UtcNow)
        {
            _lastSnapshot = current;
            _lastLoggedAtUtc = DateTime.UtcNow;
            return true;
        }

        return false;
    }
}