using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Application;

public sealed class TickLogger : ITickLogger
{
    private readonly ILogger<TickLogger> _logger;
    private readonly CoinSoulDbContext _db;

    public TickLogger(ILogger<TickLogger> logger, CoinSoulDbContext db)
    {
        _logger = logger;
        _db = db;
    }

    public void LogCritical(string message, params object[] args) 
        => _logger.LogCritical(message, args);

    public void LogInformation(string message, params object[] args) 
        => _logger.LogInformation(message, args);

    public void LogWarning(string message, params object[] args) 
        => _logger.LogWarning(message, args);

    public void LogDebug(string message, params object[] args) 
        => _logger.LogDebug(message, args);

    public void LogError(Exception ex, string message, params object[] args) 
        => _logger.LogError(ex, message, args);

    public async Task LogRegimeEventAsync(MarketRegimeDecision decision, CancellationToken ct)
    {
        try
        {
            _db.Events.Add(new EventEntity
            {
                Level = "INFO",
                Type = "MARKET_REGIME",
                Message = $"{decision.Regime} risk={decision.RiskMultiplier:0.00} tp={decision.TpMultiplier:0.00} {decision.Reason}",
                AtUtc = DateTime.UtcNow
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOG_ERROR] Failed to log regime event");
        }
    }

    public async Task LogSafetyEventAsync(string type, string? symbol, string message, CancellationToken ct)
    {
        try
        {
            _db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = type == "RISK_STOP" ? "CRITICAL" : "WARN",
                Type = type,
                Symbol = symbol,
                Message = message
            });

            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LOG_ERROR] Failed to log safety event");
        }
    }
}