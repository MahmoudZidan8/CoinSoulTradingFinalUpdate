namespace CoinSoul.Trading.Core;

public sealed class ManualSymbolConfig
{
    public string Symbol { get; set; } = "";
    public bool Enabled { get; set; } = true;

    // Risk control per symbol
    public int MaxOpenTrades { get; set; } = 1;

    // Cooldown between trades on same symbol
    public int CooldownMinutes { get; set; } = 10;

    public string? Notes { get; set; }
}
