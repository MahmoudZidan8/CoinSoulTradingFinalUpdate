namespace CoinSoul.Trading.Application;

public sealed class TickResult
{
    public bool Success { get; set; }
    public string Stage { get; set; } = "INIT";
    public string? BlockReason { get; set; }
    
    // ✅ FIX: Add DiagnosticData property
    public Dictionary<string, object> DiagnosticData { get; set; } = new();
}