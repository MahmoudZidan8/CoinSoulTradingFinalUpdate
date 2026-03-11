using CoinSoul.Trading.Engine.Observability.Models;

namespace CoinSoul.Trading.Engine.Observability;

/// <summary>
/// Writes structured events to database for observability
/// Thread-safe, failure-tolerant (never throws)
/// </summary>
public interface IEventWriter
{
    /// <summary>
    /// Writes a simple event to database
    /// Returns true if write succeeded, false on failure (never throws)
    /// </summary>
    Task<bool> WriteAsync(
        string type,
        string message,
        string level = "INFO",
        string? symbol = null,
        int? positionId = null,
        string? correlationId = null,
        CancellationToken ct = default);

    /// <summary>
    /// Checks if event writing is enabled via configuration
    /// </summary>
    bool IsEnabled { get; }
}