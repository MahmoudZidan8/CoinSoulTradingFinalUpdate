namespace CoinSoul.Trading.Engine.Cache;

public sealed record Ticker24h(
    string Symbol,
    decimal LastPrice,
    decimal QuoteVolume,
    decimal PriceChangePercent);

public sealed record BookTicker(
    string Symbol,
    decimal BidPrice,
    decimal BidQuantity,
    decimal AskPrice,
    decimal AskQuantity);

public sealed record KlineData(
    string Symbol,
    string Interval,
    List<decimal> Closes,
    List<decimal> Highs,
    List<decimal> Lows);