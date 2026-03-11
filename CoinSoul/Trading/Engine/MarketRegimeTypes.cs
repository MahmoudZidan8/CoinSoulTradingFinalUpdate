namespace CoinSoul.Trading.Engine;

public enum MarketRegime
{
    Unknown = 0,
    BullTrend = 1,
    BearTrend = 2,
    Sideways = 3,
    Crash = 4
}

public sealed class MarketRegimeDecision
{
    public bool AllowedToTrade { get; set; }
    public MarketRegime Regime { get; set; }
    public decimal RiskMultiplier { get; set; }
    public decimal TpMultiplier { get; set; }
    public string Reason { get; set; } = "";
    public DateTimeOffset AsOfUtc { get; set; }
}