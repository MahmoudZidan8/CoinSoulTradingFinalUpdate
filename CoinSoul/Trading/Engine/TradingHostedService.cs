using CoinSoul.Trading.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace CoinSoul.Trading.Engine;

public sealed class TradingHostedService : BackgroundService
{
    private readonly IServiceProvider _sp;

    public TradingHostedService(IServiceProvider sp)
    {
        _sp = sp;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            using var scope = _sp.CreateScope();
            var engine = scope.ServiceProvider.GetRequiredService<ITradingEngine>();

            await engine.RunAsync(stoppingToken);

            // tick from settings لو عايزها لاحقًا — هنا ثابت آمن
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        }
    }
}
