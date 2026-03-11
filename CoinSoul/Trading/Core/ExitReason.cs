namespace CoinSoul.Trading.Core;

public enum ExitReason
{
    None = 0,
    TakeProfit = 1,
    StopLoss = 2,
    TimeExit = 3,
    ManualStop = 4,
    OcoFailedFallbackMarketSell = 5,
    SafetyExit = 6,

    // ✅ NEW: لو الصفقة اتقفلت من برّه (بيع يدوي / OCO اتنفذ واحنا لسه مش قافلين DB)
    ExternalClose = 7,

    Unknown = 99
}
