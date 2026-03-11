namespace CoinSoul.Trading.Engine.V2;

public sealed record RegimeSnapshot(
    MarketRegime Regime,
    decimal RiskMultiplier,
    decimal TpMultiplier,
    bool AllowedToTrade,
    string Reason,
    DateTime CapturedAtUtc)
{
    public bool HasChanged(RegimeSnapshot? previous)
    {
        if (previous == null) return true;
        return Regime != previous.Regime ||
               Math.Abs(RiskMultiplier - previous.RiskMultiplier) > 0.01m ||
               Math.Abs(TpMultiplier - previous.TpMultiplier) > 0.01m;
    }
}

public sealed record ScanDiagnostics(
    int TotalScanned,
    int TotalPassed,
    Dictionary<string, int> RejectionCounts,
    TimeSpan Duration,
    DateTime ScannedAtUtc);