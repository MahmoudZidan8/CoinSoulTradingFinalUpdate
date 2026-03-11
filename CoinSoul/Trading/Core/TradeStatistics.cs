namespace CoinSoul.Trading.Core;

public sealed class TradeStatistics
{
    public int TotalTrades { get; private set; }
    public int Wins { get; private set; }
    public int Losses { get; private set; }

    public decimal TotalProfit { get; private set; }
    public decimal TotalLoss { get; private set; }

    public decimal NetPnl => TotalProfit - TotalLoss;

    public decimal WinRate =>
        TotalTrades == 0 ? 0 : (decimal)Wins / TotalTrades * 100m;

    public decimal AvgTrade =>
        TotalTrades == 0 ? 0 : NetPnl / TotalTrades;

    public void RegisterWin(decimal profit)
    {
        TotalTrades++;
        Wins++;
        TotalProfit += profit;
    }

    public void RegisterLoss(decimal loss)
    {
        TotalTrades++;
        Losses++;
        TotalLoss += loss;
    }

    // Convenience: register PnL directly (positive=win, negative=loss)
    public void Register(decimal pnl)
    {
        if (pnl >= 0)
            RegisterWin(pnl);
        else
            RegisterLoss(-pnl);
    }
}
