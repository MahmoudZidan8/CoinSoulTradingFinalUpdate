namespace CoinSoul.Trading.Core;

public sealed class SpotBalanceDto
{
    public string Asset { get; set; } = "";
    public decimal Free { get; set; }
    public decimal Locked { get; set; }
}
