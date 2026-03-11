using Microsoft.Extensions.Hosting;
using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public sealed class TradingEngineHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;

    public TradingEngineHostedService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _scopeFactory.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<ITradingEngine>();

            await engine.RunAsync(stoppingToken);

            // tick pace (خفيف)
            await Task.Delay(500, stoppingToken);
        }
    }
}
