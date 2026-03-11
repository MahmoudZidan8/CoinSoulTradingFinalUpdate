namespace CoinSoul.Trading.Application;

public sealed class TickContext
{
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime TickStartUtc { get; init; }
    public string CurrentStage { get; set; } = "Start";
    
    public Dictionary<string, object> Metrics { get; init; } = new();
    
    public void SetMetric(string key, object value)
    {
        Metrics[key] = value;
    }
}