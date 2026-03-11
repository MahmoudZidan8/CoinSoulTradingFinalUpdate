using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace CoinSoul.Trading.Engine;

public sealed class PerformanceMetricsService
{
    private readonly CoinSoulDbContext _db;

    public PerformanceMetricsService(CoinSoulDbContext db)
    {
        _db = db;
    }

    public async Task<PerformanceMetrics> GetMetricsAsync(
        CancellationToken ct,
        DateTime? fromDate = null,
        DateTime? toDate = null,
        string? symbol = null,
        int? strategyMode = null)
    {
        var now = DateTime.UtcNow;
        var today = now.Date;
        var last7Days = now.AddDays(-7);
        var last30Days = now.AddDays(-30);

        var query = _db.Positions
            .Where(p => !p.IsOpen && p.ClosedAtUtc != null);

        if (fromDate.HasValue)
            query = query.Where(p => p.ClosedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(p => p.ClosedAtUtc <= toDate.Value);

        if (!string.IsNullOrWhiteSpace(symbol))
            query = query.Where(p => p.Symbol == symbol);

        var allClosed = await query
            .OrderBy(p => p.ClosedAtUtc)
            .ToListAsync(ct);

        var closedToday = allClosed.Where(p => p.ClosedAtUtc!.Value.Date == today).ToList();
        var closed7Days = allClosed.Where(p => p.ClosedAtUtc >= last7Days).ToList();
        var closed30Days = allClosed.Where(p => p.ClosedAtUtc >= last30Days).ToList();

        var wins = closed30Days.Where(p => p.NetPnlUsdt > 0).ToList();
        var losses = closed30Days.Where(p => p.NetPnlUsdt < 0).ToList();

        var winRate = closed30Days.Any() ? (decimal)wins.Count / closed30Days.Count * 100m : 0;
        var avgWin = wins.Any() ? wins.Average(p => p.NetPnlUsdt) : 0;
        var avgLoss = losses.Any() ? Math.Abs(losses.Average(p => p.NetPnlUsdt)) : 0;
        
        var grossWin = wins.Sum(p => p.NetPnlUsdt);
        var grossLoss = Math.Abs(losses.Sum(p => p.NetPnlUsdt));
        var profitFactor = grossLoss > 0 ? grossWin / grossLoss : 0;

        var maxDrawdown = CalculateMaxDrawdown(allClosed);
        var netProfit = allClosed.Sum(p => p.NetPnlUsdt);

        var equityCurve = BuildEquityCurve(allClosed);
        var dailyReturns = BuildDailyReturns(allClosed);
        var riskMetricsData = CalculateAdvancedRiskMetrics(dailyReturns, netProfit, maxDrawdown);

        var monthlyBreakdown = BuildMonthlyBreakdown(allClosed);
        var symbolStats = BuildSymbolStats(allClosed);
        var hourlyStats = BuildHourlyStats(allClosed);

        var bestWorstDays = GetBestWorstDays(allClosed);
        var streaksData = CalculateStreaks(allClosed);

        return new PerformanceMetrics
        {
            TradesToday = closedToday.Count,
            TodayNetPnl = closedToday.Sum(p => p.NetPnlUsdt),
            
            Trades7Days = closed7Days.Count,
            NetPnl7Days = closed7Days.Sum(p => p.NetPnlUsdt),
            
            Trades30Days = closed30Days.Count,
            NetPnl30Days = closed30Days.Sum(p => p.NetPnlUsdt),
            
            TotalTrades = allClosed.Count,
            NetProfit = netProfit,
            
            WinRate = winRate,
            AvgWinUsdt = avgWin,
            AvgLossUsdt = avgLoss,
            MaxWinUsdt = wins.Any() ? wins.Max(p => p.NetPnlUsdt) : 0,
            MaxLossUsdt = losses.Any() ? losses.Min(p => p.NetPnlUsdt) : 0,
            
            ProfitFactor = profitFactor,
            Expectancy = closed30Days.Any() ? closed30Days.Average(p => p.NetPnlUsdt) : 0,
            MaxDrawdownUsdt = maxDrawdown,
            
            AvgTradeDurationMinutes = closed30Days.Any() 
                ? (decimal)closed30Days.Average(p => (p.ClosedAtUtc!.Value - p.OpenedAtUtc).TotalMinutes) 
                : 0,
            
            OpenPositions = await _db.Positions.CountAsync(p => p.IsOpen, ct),

            EquityCurve = equityCurve,
            MonthlyBreakdown = monthlyBreakdown,
            SymbolStats = symbolStats,
            HourlyStats = hourlyStats,
            DailyPnlData = dailyReturns,

            SharpeRatio = riskMetricsData.SharpeRatio,
            SortinoRatio = riskMetricsData.SortinoRatio,
            RecoveryFactor = riskMetricsData.RecoveryFactor,
            RiskRewardRatio = avgLoss > 0 ? avgWin / avgLoss : 0,
            BestDay = bestWorstDays.BestDay,
            WorstDay = bestWorstDays.WorstDay,
            WinStreakMax = streaksData.WinStreak,
            LossStreakMax = streaksData.LossStreak,
            AvgDailyPnl = riskMetricsData.AvgDailyPnl,
            StdDevDailyReturn = riskMetricsData.StdDevDailyReturn,
            CalmarRatio = riskMetricsData.CalmarRatio,

            RiskMetrics = new AdvancedRiskMetrics
            {
                SharpeRatio = riskMetricsData.SharpeRatio,
                SortinoRatio = riskMetricsData.SortinoRatio,
                RecoveryFactor = riskMetricsData.RecoveryFactor,
                RiskRewardRatio = avgLoss > 0 ? avgWin / avgLoss : 0,
                BestDay = bestWorstDays.BestDay,
                WorstDay = bestWorstDays.WorstDay,
                WinStreakMax = streaksData.WinStreak,
                LossStreakMax = streaksData.LossStreak,
                AvgDailyPnl = riskMetricsData.AvgDailyPnl,
                StdDevDailyReturn = riskMetricsData.StdDevDailyReturn,
                CalmarRatio = riskMetricsData.CalmarRatio
            }
        };
    }

    public async Task<string> ExportToCsvAsync(DateTime? fromDate, DateTime? toDate, string? symbol, CancellationToken ct)
    {
        var query = _db.Positions
            .Where(p => !p.IsOpen && p.ClosedAtUtc != null);

        if (fromDate.HasValue)
            query = query.Where(p => p.ClosedAtUtc >= fromDate.Value);

        if (toDate.HasValue)
            query = query.Where(p => p.ClosedAtUtc <= toDate.Value);

        if (!string.IsNullOrWhiteSpace(symbol))
            query = query.Where(p => p.Symbol == symbol);

        var trades = await query.OrderBy(p => p.ClosedAtUtc).ToListAsync(ct);

        var sb = new StringBuilder();
        sb.AppendLine("ClosedAt,Symbol,EntryPrice,ExitPrice,Quantity,NetPnL,Fees,Duration");

        foreach (var t in trades)
        {
            var duration = t.ClosedAtUtc.HasValue 
                ? (t.ClosedAtUtc.Value - t.OpenedAtUtc).TotalMinutes 
                : 0;

            sb.AppendLine($"{t.ClosedAtUtc:yyyy-MM-dd HH:mm:ss},{t.Symbol},{t.EntryPrice},{t.ExitPrice},{t.Quantity},{t.NetPnlUsdt},{t.FeesUsdt},{duration:0.0}");
        }

        return sb.ToString();
    }

    private static decimal CalculateMaxDrawdown(List<Entities.PositionEntity> trades)
    {
        if (!trades.Any()) return 0;

        decimal peak = 0;
        decimal maxDD = 0;
        decimal cumulative = 0;

        foreach (var trade in trades)
        {
            cumulative += trade.NetPnlUsdt;
            if (cumulative > peak) peak = cumulative;
            
            var dd = peak - cumulative;
            if (dd > maxDD) maxDD = dd;
        }

        return maxDD;
    }

    private static List<EquityPoint> BuildEquityCurve(List<Entities.PositionEntity> trades)
    {
        var result = new List<EquityPoint>();
        if (!trades.Any()) return result;

        var grouped = trades
            .Where(t => t.ClosedAtUtc != null)
            .GroupBy(t => t.ClosedAtUtc!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                Date = g.Key,
                Net = g.Sum(x => x.NetPnlUsdt)
            })
            .ToList();

        decimal cumulative = 0;
        decimal peak = 0;

        foreach (var day in grouped)
        {
            cumulative += day.Net;
            if (cumulative > peak)
                peak = cumulative;

            var drawdown = peak - cumulative;

            result.Add(new EquityPoint
            {
                Date = day.Date,
                DailyNet = day.Net,
                Cumulative = cumulative,
                Drawdown = drawdown
            });
        }

        return result;
    }

    private static List<DailyReturn> BuildDailyReturns(List<Entities.PositionEntity> trades)
    {
        return trades
            .Where(t => t.ClosedAtUtc != null)
            .GroupBy(t => t.ClosedAtUtc!.Value.Date)
            .OrderBy(g => g.Key)
            .Select(g => new DailyReturn
            {
                Date = g.Key,
                NetPnl = g.Sum(x => x.NetPnlUsdt)
            })
            .ToList();
    }

    private static RiskMetricsInternal CalculateAdvancedRiskMetrics(
        List<DailyReturn> dailyReturns,
        decimal netProfit,
        decimal maxDrawdown)
    {
        if (!dailyReturns.Any())
        {
            return new RiskMetricsInternal
            {
                SharpeRatio = 0,
                SortinoRatio = 0,
                RecoveryFactor = 0,
                AvgDailyPnl = 0,
                StdDevDailyReturn = 0,
                CalmarRatio = 0
            };
        }

        var avgDaily = dailyReturns.Average(d => d.NetPnl);
        var variance = dailyReturns.Sum(d => (d.NetPnl - avgDaily) * (d.NetPnl - avgDaily)) / dailyReturns.Count;
        var stdDev = (decimal)Math.Sqrt((double)variance);

        var downsideDays = dailyReturns.Where(d => d.NetPnl < 0).ToList();
        var downsideVariance = downsideDays.Any()
            ? downsideDays.Sum(d => d.NetPnl * d.NetPnl) / downsideDays.Count
            : 0;
        var downsideDev = (decimal)Math.Sqrt((double)downsideVariance);

        var sharpe = stdDev > 0 ? avgDaily / stdDev : 0;
        var sortino = downsideDev > 0 ? avgDaily / downsideDev : 0;
        var recovery = maxDrawdown > 0 ? netProfit / maxDrawdown : 0;
        var calmar = maxDrawdown > 0 ? netProfit / maxDrawdown : 0;

        return new RiskMetricsInternal
        {
            SharpeRatio = sharpe,
            SortinoRatio = sortino,
            RecoveryFactor = recovery,
            AvgDailyPnl = avgDaily,
            StdDevDailyReturn = stdDev,
            CalmarRatio = calmar
        };
    }

    private static List<MonthlyPerformance> BuildMonthlyBreakdown(List<Entities.PositionEntity> trades)
    {
        return trades
            .Where(t => t.ClosedAtUtc != null)
            .GroupBy(t => new { t.ClosedAtUtc!.Value.Year, t.ClosedAtUtc.Value.Month })
            .OrderByDescending(g => g.Key.Year).ThenByDescending(g => g.Key.Month)
            .Select(g =>
            {
                var wins = g.Count(x => x.NetPnlUsdt > 0);
                var total = g.Count();
                return new MonthlyPerformance
                {
                    YearMonth = $"{g.Key.Year}-{g.Key.Month:00}",
                    NetPnl = g.Sum(x => x.NetPnlUsdt),
                    Trades = total,
                    WinRate = total > 0 ? (decimal)wins / total * 100m : 0
                };
            })
            .ToList();
    }

    private static List<SymbolPerformance> BuildSymbolStats(List<Entities.PositionEntity> trades)
    {
        return trades
            .GroupBy(t => t.Symbol)
            .Select(g =>
            {
                var wins = g.Count(x => x.NetPnlUsdt > 0);
                var grossWin = g.Where(x => x.NetPnlUsdt > 0).Sum(x => x.NetPnlUsdt);
                var grossLoss = Math.Abs(g.Where(x => x.NetPnlUsdt < 0).Sum(x => x.NetPnlUsdt));

                return new SymbolPerformance
                {
                    Symbol = g.Key,
                    NetPnl = g.Sum(x => x.NetPnlUsdt),
                    Trades = g.Count(),
                    WinRate = g.Count() > 0 ? (decimal)wins / g.Count() * 100m : 0,
                    ProfitFactor = grossLoss > 0 ? grossWin / grossLoss : 0
                };
            })
            .OrderByDescending(s => s.NetPnl)
            .ToList();
    }

    private static List<HourlyPerformance> BuildHourlyStats(List<Entities.PositionEntity> trades)
    {
        return trades
            .Where(t => t.ClosedAtUtc != null)
            .GroupBy(t => t.ClosedAtUtc!.Value.Hour)
            .Select(g =>
            {
                var wins = g.Count(x => x.NetPnlUsdt > 0);
                return new HourlyPerformance
                {
                    Hour = g.Key,
                    NetPnl = g.Sum(x => x.NetPnlUsdt),
                    Trades = g.Count(),
                    WinRate = g.Count() > 0 ? (decimal)wins / g.Count() * 100m : 0
                };
            })
            .OrderBy(h => h.Hour)
            .ToList();
    }

    private static BestWorstDaysResult GetBestWorstDays(List<Entities.PositionEntity> trades)
    {
        if (!trades.Any())
        {
            return new BestWorstDaysResult
            {
                BestDay = 0,
                WorstDay = 0
            };
        }

        var dailyPnl = trades
            .Where(t => t.ClosedAtUtc != null)
            .GroupBy(t => t.ClosedAtUtc!.Value.Date)
            .Select(g => g.Sum(x => x.NetPnlUsdt))
            .ToList();

        var best = dailyPnl.Any() ? dailyPnl.Max() : 0;
        var worst = dailyPnl.Any() ? dailyPnl.Min() : 0;

        return new BestWorstDaysResult
        {
            BestDay = best,
            WorstDay = worst
        };
    }

    private static StreaksResult CalculateStreaks(List<Entities.PositionEntity> trades)
    {
        if (!trades.Any())
        {
            return new StreaksResult
            {
                WinStreak = 0,
                LossStreak = 0
            };
        }

        var ordered = trades.OrderBy(t => t.ClosedAtUtc).ToList();

        int maxWinStreak = 0;
        int maxLossStreak = 0;
        int currentWinStreak = 0;
        int currentLossStreak = 0;

        foreach (var trade in ordered)
        {
            if (trade.NetPnlUsdt > 0)
            {
                currentWinStreak++;
                currentLossStreak = 0;
                if (currentWinStreak > maxWinStreak)
                    maxWinStreak = currentWinStreak;
            }
            else if (trade.NetPnlUsdt < 0)
            {
                currentLossStreak++;
                currentWinStreak = 0;
                if (currentLossStreak > maxLossStreak)
                    maxLossStreak = currentLossStreak;
            }
        }

        return new StreaksResult
        {
            WinStreak = maxWinStreak,
            LossStreak = maxLossStreak
        };
    }

    // Private helper result classes
    private sealed class RiskMetricsInternal
    {
        public decimal SharpeRatio { get; set; }
        public decimal SortinoRatio { get; set; }
        public decimal RecoveryFactor { get; set; }
        public decimal AvgDailyPnl { get; set; }
        public decimal StdDevDailyReturn { get; set; }
        public decimal CalmarRatio { get; set; }
    }

    private sealed class BestWorstDaysResult
    {
        public decimal BestDay { get; set; }
        public decimal WorstDay { get; set; }
    }

    private sealed class StreaksResult
    {
        public int WinStreak { get; set; }
        public int LossStreak { get; set; }
    }

    private static string GetPnLColor(decimal value)
    {
        if (value > 0) return "#22c55e";
        if (value < 0) return "#ef4444";
        return "#94a3b8";
    }
}

// Public models
public sealed class PerformanceMetrics
{
    public int TradesToday { get; set; }
    public decimal TodayNetPnl { get; set; }
    
    public int Trades7Days { get; set; }
    public decimal NetPnl7Days { get; set; }
    
    public int Trades30Days { get; set; }
    public decimal NetPnl30Days { get; set; }
    
    public int TotalTrades { get; set; }
    public decimal NetProfit { get; set; }
    
    public decimal WinRate { get; set; }
    public decimal AvgWinUsdt { get; set; }
    public decimal AvgLossUsdt { get; set; }
    public decimal MaxWinUsdt { get; set; }
    public decimal MaxLossUsdt { get; set; }
    
    public decimal ProfitFactor { get; set; }
    public decimal Expectancy { get; set; }
    public decimal MaxDrawdownUsdt { get; set; }
    
    public decimal AvgTradeDurationMinutes { get; set; }
    
    public int OpenPositions { get; set; }

    public List<EquityPoint> EquityCurve { get; set; } = new();

    // Advanced Risk Metrics (direct properties for backward compatibility)
    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    public decimal RecoveryFactor { get; set; }
    public decimal RiskRewardRatio { get; set; }
    public decimal BestDay { get; set; }
    public decimal WorstDay { get; set; }
    public int WinStreakMax { get; set; }
    public int LossStreakMax { get; set; }
    public decimal AvgDailyPnl { get; set; }
    public decimal StdDevDailyReturn { get; set; }
    public decimal CalmarRatio { get; set; }

    public List<MonthlyPerformance> MonthlyBreakdown { get; set; } = new();
    public List<SymbolPerformance> SymbolStats { get; set; } = new();
    public List<HourlyPerformance> HourlyStats { get; set; } = new();
    public List<DailyReturn> DailyPnlData { get; set; } = new();

    // Nested object for structured access
    public AdvancedRiskMetrics RiskMetrics { get; set; } = new();
}

public sealed class AdvancedRiskMetrics
{
    public decimal SharpeRatio { get; set; }
    public decimal SortinoRatio { get; set; }
    public decimal RecoveryFactor { get; set; }
    public decimal RiskRewardRatio { get; set; }
    public decimal BestDay { get; set; }
    public decimal WorstDay { get; set; }
    public int WinStreakMax { get; set; }
    public int LossStreakMax { get; set; }
    public decimal AvgDailyPnl { get; set; }
    public decimal StdDevDailyReturn { get; set; }
    public decimal CalmarRatio { get; set; }
}

public sealed class EquityPoint
{
    public DateTime Date { get; set; }
    public decimal DailyNet { get; set; }
    public decimal Cumulative { get; set; }
    public decimal Drawdown { get; set; }
}

public sealed class MonthlyPerformance
{
    public string YearMonth { get; set; } = "";
    public decimal NetPnl { get; set; }
    public int Trades { get; set; }
    public decimal WinRate { get; set; }
}

public sealed class SymbolPerformance
{
    public string Symbol { get; set; } = "";
    public decimal NetPnl { get; set; }
    public int Trades { get; set; }
    public decimal WinRate { get; set; }
    public decimal ProfitFactor { get; set; }
}

public sealed class HourlyPerformance
{
    public int Hour { get; set; }
    public decimal NetPnl { get; set; }
    public int Trades { get; set; }
    public decimal WinRate { get; set; }
}

public sealed class DailyReturn
{
    public DateTime Date { get; set; }
    public decimal NetPnl { get; set; }
}