namespace CoinSoul.Trading.Application;

/// <summary>
/// Clock abstraction for testability
/// </summary>
public interface IClock
{
    DateTime UtcNow { get; }
    DateTimeOffset UtcNowOffset { get; }
}