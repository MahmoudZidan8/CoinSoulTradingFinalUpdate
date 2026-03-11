using CoinSoul.Trading.Core;

namespace CoinSoul.Trading.Engine;

public interface ISymbolProvider
{
    Task<List<SymbolInfo>> GetSpotSymbolsAsync();
}
