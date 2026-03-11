namespace CoinSoul.BinanceService.Enums
{
    public static class CoinSymbols
    {
        public static string BtcUsdt = "BTCUSDT";

        public static string EthUsdt = "ETHUSDT";

        public static string BnbUsdt = "BNBUSDT";

        public static string AaveUsdt = "AAVEUSDT";


        public static List<string> GetList()
        {
            return [BtcUsdt, EthUsdt, BnbUsdt, AaveUsdt];
        }
    }
}