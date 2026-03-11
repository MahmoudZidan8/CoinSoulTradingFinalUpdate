namespace CoinSoul.Trading.Core;

public static class PerformanceCalculator
{
    public static PerformanceSnapshot Calculate(IEnumerable<TradeHistoryItem> trades)
    {
        var list = trades.ToList();
        if (!list.Any())
            return new PerformanceSnapshot();

        var wins = list.Where(x => x.PnL > 0).ToList();
        var losses = list.Where(x => x.PnL < 0).ToList();

        var equity = 0m;
        var peak = 0m;
        var maxDd = 0m;

        foreach (var t in list.OrderBy(x => x.ClosedAtUtc))
        {
            equity += t.PnL;
            peak = Math.Max(peak, equity);
            maxDd = Math.Min(maxDd, equity - peak);
        }

        return new PerformanceSnapshot
        {
            TotalTrades = list.Count,
            Wins = wins.Count,
            Losses = losses.Count,

            WinRatePct = list.Count == 0
                ? 0
                : (decimal)wins.Count / list.Count * 100m,

            TotalPnL = list.Sum(x => x.PnL),
            AvgWin = wins.Any() ? wins.Average(x => x.PnL) : 0,
            AvgLoss = losses.Any() ? losses.Average(x => x.PnL) : 0,

            MaxDrawdown = maxDd
        };
    }
}
