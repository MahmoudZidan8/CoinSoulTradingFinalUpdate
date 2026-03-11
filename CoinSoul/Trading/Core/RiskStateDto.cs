namespace CoinSoul.Trading.Core;

/// <summary>
/// DTO for risk state data transfer to UI
/// </summary>
public sealed record RiskStateDto
{
    public string Status { get; init; } = "SAFE";
    public string StatusColor { get; init; } = "green";
    public decimal CurrentEquityUsdt { get; init; }
    public decimal StartOfDayEquityUsdt { get; init; }
    public decimal DrawdownPct { get; init; }
    public DateTime? PauseUntilUtc { get; init; }
    public DateTime? StopUntilUtc { get; init; }
    public string Message { get; init; } = "";
}