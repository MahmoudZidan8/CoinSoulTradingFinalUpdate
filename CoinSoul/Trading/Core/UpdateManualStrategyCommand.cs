namespace CoinSoul.Trading.Core;

public sealed class UpdateManualStrategyCommand : ITradingCommand
{
    public List<ManualSymbolConfig> Symbols { get; }

    public UpdateManualStrategyCommand(List<ManualSymbolConfig> symbols)
    {
        Symbols = symbols;
    }
}
