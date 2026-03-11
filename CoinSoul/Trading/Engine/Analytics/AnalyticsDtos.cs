namespace CoinSoul.Trading.Engine.Analytics;

public sealed class PerformanceDashboardDto
{
    public DateTimeOffset GeneratedAtUtc { get; set; }
    public PeriodMetricsDto Today { get; set; } = new();
    public PeriodMetricsDto Last7Days { get; set; } = new();
    public PeriodMetricsDto Last30Days { get; set; } = new();

    public DrawdownDto Drawdown { get; set; } = new();
    public ExecutionQualityDto ExecutionQuality { get; set; } = new();
    public RegimeStatsDto RegimeStats { get; set; } = new();

    public List<EquityPointDto> EquityCurveToday { get; set; } = new();
    public List<EquityPointDto> EquityCurve7D { get; set; } = new();
    public List<EquityPointDto> EquityCurve30D { get; set; } = new();

    public List<TopSymbolPnlDto> TopWinners7D { get; set; } = new();
    public List<TopSymbolPnlDto> TopLosers7D { get; set; } = new();

    public List<RejectReasonDto> TopRejectReasonsToday { get; set; } = new();
}

public sealed class PeriodMetricsDto
{
    public int Trades { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public decimal WinRatePct { get; set; }
    public decimal GrossPnlUsdt { get; set; }
    public decimal FeesUsdt { get; set; }
    public decimal NetPnlUsdt { get; set; }
    public decimal AvgNetPnlUsdt { get; set; }
    public decimal MaxWinUsdt { get; set; }
    public decimal MaxLossUsdt { get; set; }
}

public sealed class DrawdownDto
{
    public decimal StartEquityUsdt { get; set; }
    public decimal CurrentEquityUsdt { get; set; }
    public decimal MaxEquityUsdt { get; set; }
    public decimal MaxDrawdownPct { get; set; }
    public decimal CurrentDrawdownPct { get; set; }
}

public sealed class ExecutionQualityDto
{
    public int EntryAttempts { get; set; }
    public int EntryAccepted { get; set; }
    public int EntryRejected { get; set; }
    public int OcoPlaced { get; set; }
    public int OcoOk { get; set; }
    public int OcoFail { get; set; }
    public int SafetyExits { get; set; }
    public decimal OcoSuccessRatePct { get; set; }
}

public sealed class RegimeStatsDto
{
    public int BullCount { get; set; }
    public int BearCount { get; set; }
    public int SidewaysCount { get; set; }
    public int CrashCount { get; set; }
    public decimal BullPct { get; set; }
    public decimal BearPct { get; set; }
    public decimal SidewaysPct { get; set; }
    public decimal CrashPct { get; set; }
}

public sealed class EquityPointDto
{
    public DateTimeOffset AtUtc { get; set; }
    public decimal EquityUsdt { get; set; }
}

public sealed class TopSymbolPnlDto
{
    public string Symbol { get; set; } = "";
    public decimal NetPnlUsdt { get; set; }
    public int Trades { get; set; }
}

public sealed class RejectReasonDto
{
    public string Reason { get; set; } = "";
    public int Count { get; set; }
}