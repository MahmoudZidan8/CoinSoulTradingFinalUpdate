namespace CoinSoul.Trading.Core;

public interface ITradingEngine
{
    event Action? OnStateChanged;

    BotState GetState();

    Task EnqueueAsync(ITradingCommand command, CancellationToken ct = default);

    void Start();
    void Stop();

    Task RunAsync(CancellationToken ct);

    Task<DashboardStats> GetDashboardStatsAsync(CancellationToken ct);
}
