using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Engine.Observability;

/// <summary>
/// Guarantees at least one event is written per trading tick
/// Usage: await using var guard = new TickEventGuard(...);
/// On dispose, writes heartbeat event if no other events were recorded
/// </summary>
public sealed class TickEventGuard : IAsyncDisposable
{
    private readonly IEventWriter _eventWriter;
    private readonly ILogger<TickEventGuard> _logger;
    private readonly string _correlationId;
    private int _eventsRecorded = 0;

    public string CorrelationId => _correlationId;

    public TickEventGuard(
        IEventWriter eventWriter,
        ILogger<TickEventGuard> logger)
    {
        _eventWriter = eventWriter;
        _logger = logger;
        _correlationId = Guid.NewGuid().ToString("N")[..12]; // 12-char short ID
    }

    /// <summary>
    /// Records an event and increments counter
    /// </summary>
    public async Task<bool> MarkAsync(
        string type,
        string message,
        string level = "INFO",
        string? symbol = null,
        int? positionId = null,
        CancellationToken ct = default)
    {
        var success = await _eventWriter.WriteAsync(type, message, level, symbol, positionId, _correlationId, ct);
        if (success)
        {
            Interlocked.Increment(ref _eventsRecorded);
        }
        return success;
    }

    public async ValueTask DisposeAsync()
    {
        // ✅ GUARANTEE: If no events recorded, write heartbeat
        if (_eventsRecorded == 0)
        {
            _logger.LogWarning("[TICK_HEARTBEAT] No events recorded for tick {CorrelationId}, writing heartbeat",
                _correlationId);

            await _eventWriter.WriteAsync(
                "TICK_HEARTBEAT",
                "No events recorded during this tick (silent execution path)",
                "WARN",
                correlationId: _correlationId,
                ct: CancellationToken.None);
        }
        else
        {
            _logger.LogDebug("[TICK_GUARD_DISPOSED] Tick {CorrelationId} recorded {Count} events",
                _correlationId, _eventsRecorded);
        }
    }
}