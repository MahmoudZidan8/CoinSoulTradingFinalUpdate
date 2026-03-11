using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CoinSoul.BinanceService.API
{
    public static class APINames
    {
        public static readonly string AccountId = "775649674";
        
        //Live Network URls
        //public static readonly string ApiKey = "s8tPVUhngB8sh8JY7gPtgrTBMeEVVYeLVuV9hvA9EUG7vnkjvUT2kFddGOudJIKM";
        //public static readonly string SecretKey = "4ylLJUmRIJ3w5q7x2yXw7nrpshP3MDaWDVMTBL2wmOTm4GD6gElRs56busEj8h8t";
        public static string ApiKey { get; set; } = string.Empty;
        public static string SecretKey { get; set; } = string.Empty;

        public static string TestApiKey { get; set; } = string.Empty;
        public static string TestSecretKey { get; set; } = string.Empty;

        public static readonly string Url = "https://api.binance.com/api";
        public static readonly string WebSocketUrl = "wss://ws-api.binance.com/ws-api/v3";
        public static readonly string WebSocketV3Url = "wss://stream.binance.com:9443/ws";
        public static readonly string StreamUrl = "wss://stream.binance.com:9443/stream";

        //Test Network URLs
        //public static readonly string TestApiKey = "S12BY5UIGXNmIjdm36KnmhpV3TITwllF8m4ifKxbdBVoCS3UX0WOM6q2smhNGueW";
        //public static readonly string TestSecretKey = "psglap4LtRq9KI8p29ASEW4IDQUzm4LTmCYPs8XqcRwcF1qWRVFE3V2Gu2GU3Uda";

        public static readonly string TestUrl = "https://testnet.binance.vision/api";
        public static readonly string TestWebSocketUrl = "wss://testnet.binance.vision/ws-api/v3";
        public static readonly string TestWebSocketV3Url = "wss://testnet.binance.vision/ws";
        public static readonly string TestStreamUrl = "wss://testnet.binance.vision/stream";

    }
}
