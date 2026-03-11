using CoinSoul.Entities;
using CoinSoul.Trading.Core;

public static class PositionEntityExtensions
{
    public static ExitReason GetExitReason(this PositionEntity p)
    {
        if (string.IsNullOrWhiteSpace(p.ExitReasonValue))
            return ExitReason.ManualStop;

        return Enum.TryParse(
            p.ExitReasonValue,
            ignoreCase: true,
            out ExitReason reason)
            ? reason
            : ExitReason.ManualStop;
    }

}
