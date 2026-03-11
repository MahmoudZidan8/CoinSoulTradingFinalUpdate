using CoinSoul.Entities;
using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public static class RiskStateMapper
{
    public static RiskState ToEntity(this RiskStateDto dto)
    {
        return new RiskState
        {
            //TimestampUtc = DateTime.UtcNow,
            Status = dto.Status,
            CurrentEquityUsdt = dto.CurrentEquityUsdt,
            StartOfDayEquityUsdt = dto.StartOfDayEquityUsdt,
            DrawdownPct = dto.DrawdownPct,
            PauseUntilUtc = dto.PauseUntilUtc,
            StopUntilUtc = dto.StopUntilUtc,
            Message = dto.Message
        };
    }
}
