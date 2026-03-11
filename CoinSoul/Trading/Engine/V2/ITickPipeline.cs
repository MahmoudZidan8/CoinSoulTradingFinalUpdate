using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine.V2;

public interface ITickPipeline
{
    Task<TickResult> ExecuteTickAsync(BotState state, CancellationToken ct);
}

public sealed class TickResult
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime TickStartUtc { get; init; }
    public DateTime TickEndUtc { get; init; }
    public TimeSpan Duration => TickEndUtc - TickStartUtc;
    
    public string Stage { get; init; } = "";
    public bool Success { get; init; }
    public string? BlockReason { get; init; }
    public string? Symbol { get; init; }
    public int? PositionId { get; init; }
    
    public Dictionary<string, object> Metrics { get; init; } = new();
}