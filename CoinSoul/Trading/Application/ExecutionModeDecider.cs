using CoinSoul.Entities;

namespace CoinSoul.Trading.Application;

public sealed class ExecutionModeDecider : IExecutionModeDecider
{
    private readonly bool _coreV2Enabled;
    private readonly bool _coreV2ShadowMode;

    public ExecutionModeDecider()
    {
        _coreV2Enabled = Environment.GetEnvironmentVariable("COINSOUL_COREV2_ENABLED") == "true";
        _coreV2ShadowMode = Environment.GetEnvironmentVariable("COINSOUL_COREV2_SHADOW") == "true";
    }

    public bool ShouldExecuteTrades(BotSettingsEntity settings)
    {
        return settings.ExecuteTrades && !_coreV2ShadowMode;
    }

    public bool IsShadowMode() => _coreV2ShadowMode;

    public bool IsCoreV2Enabled() => _coreV2Enabled;
}