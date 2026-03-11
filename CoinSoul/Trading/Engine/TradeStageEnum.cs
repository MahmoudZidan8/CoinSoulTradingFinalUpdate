namespace CoinSoul.Trading.Engine;

public enum TradeStage
{
    EntryPending = 0,
    EntryFilled = 1,
    OcoPlacing = 2,
    OcoPlaced = 3,
    Closing = 4,
    Closed = 5,
    Failed = 99
}