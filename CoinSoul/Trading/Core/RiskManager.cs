namespace CoinSoul.Trading.Core;

public class RiskManager
{
    public bool CanOpenTrade(
        BotState state,
        string symbol,
        decimal riskUsd)
    {
        var settings = state.Settings;

        if (state.OpenPositions.Count >= settings.MaxOpenTrades)
            return false;

        if (state.DailyLossUsd >= settings.DailyLossLimitUsd)
            return false;

        if (state.IsSymbolInCooldown(symbol))
            return false;

        return true;
    }
}
