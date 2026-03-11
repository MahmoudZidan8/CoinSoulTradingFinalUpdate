using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;

namespace CoinSoul.Trading.Application;

public interface ITickLogger
{
    void LogCritical(string message, params object[] args);
    void LogInformation(string message, params object[] args);
    void LogWarning(string message, params object[] args);
    void LogDebug(string message, params object[] args);
    void LogError(Exception ex, string message, params object[] args);
    
    Task LogRegimeEventAsync(MarketRegimeDecision decision, CancellationToken ct);
    Task LogSafetyEventAsync(string type, string? symbol, string message, CancellationToken ct);
}