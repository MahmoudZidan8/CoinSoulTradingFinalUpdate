namespace CoinSoul.Trading.Engine.Observability.Models;

/// <summary>
/// Structured event for trading engine observability
/// Maps to existing Events table schema
/// </summary>
public sealed record EngineEvent
{
    public required DateTime AtUtc { get; init; }
    public required string Type { get; init; }
    public required string Level { get; init; }
    public required string Message { get; init; }
    public string? Symbol { get; init; }
    public int? PositionId { get; init; }
    public string? CorrelationId { get; init; }
    public Dictionary<string, object>? Metadata { get; init; }

    // Factory methods for common event types
    public static EngineEvent TickStart(DateTime utc, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "TICK_START",
        Level = "INFO",
        Message = "Trading tick started",
        CorrelationId = correlationId
    };

    public static EngineEvent TickDone(DateTime utc, string correlationId, Dictionary<string, object>? metadata = null) => new()
    {
        AtUtc = utc,
        Type = "TICK_DONE",
        Level = "INFO",
        Message = "Trading tick completed",
        CorrelationId = correlationId,
        Metadata = metadata
    };

    public static EngineEvent TickBlocked(DateTime utc, string correlationId, string reason) => new()
    {
        AtUtc = utc,
        Type = "TICK_BLOCKED",
        Level = "WARN",
        Message = $"Trading tick blocked: {reason}",
        CorrelationId = correlationId,
        Metadata = new() { ["BlockReason"] = reason }
    };

    public static EngineEvent TickOutsideHours(DateTime utc, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "TICK_OUTSIDE_HOURS",
        Level = "INFO",
        Message = "Trading tick skipped (outside trading hours)",
        CorrelationId = correlationId
    };

    public static EngineEvent TickHeartbeat(DateTime utc, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "TICK_HEARTBEAT",
        Level = "INFO",
        Message = "Trading tick heartbeat (no other events recorded)",
        CorrelationId = correlationId
    };

    public static EngineEvent QueueEmpty(DateTime utc, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "QUEUE_EMPTY",
        Level = "INFO",
        Message = "Symbol queue is empty",
        CorrelationId = correlationId
    };

    public static EngineEvent QueueEnqueueBatch(DateTime utc, int count, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "QUEUE_ENQUEUE_BATCH",
        Level = "INFO",
        Message = $"Enqueued {count} symbols to queue",
        CorrelationId = correlationId,
        Metadata = new() { ["Count"] = count }
    };

    public static EngineEvent QueueEnqueueEmpty(DateTime utc, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "QUEUE_ENQUEUE_EMPTY",
        Level = "WARN",
        Message = "Attempted to enqueue 0 symbols",
        CorrelationId = correlationId
    };

    public static EngineEvent ScanResult(DateTime utc, int scanned, int passed, Dictionary<string, int> rejectionCounts, string correlationId) => new()
    {
        AtUtc = utc,
        Type = "SCAN_RESULT",
        Level = passed > 0 ? "INFO" : "WARN",
        Message = $"Opportunity scan: {passed}/{scanned} candidates passed",
        CorrelationId = correlationId,
        Metadata = new()
        {
            ["Scanned"] = scanned,
            ["Passed"] = passed,
            ["RejectionCounts"] = rejectionCounts
        }
    };
}