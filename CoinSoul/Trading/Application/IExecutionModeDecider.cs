using CoinSoul.Entities;

namespace CoinSoul.Trading.Application;

public interface IExecutionModeDecider
{
    bool ShouldExecuteTrades(BotSettingsEntity settings);
    bool IsShadowMode();
    bool IsCoreV2Enabled();
}