using Binance.Net.Interfaces.Clients;
using CoinSoul.Entities;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Core;

public sealed class HybridEntryService
{
    private readonly ITradeExecutor _executor;
    private readonly IBinanceRestClient _binanceClient;
    private readonly ILogger<HybridEntryService> _logger;

    public HybridEntryService(
        ITradeExecutor executor,
        IBinanceRestClient binanceClient,
        ILogger<HybridEntryService> logger)
    {
        _executor = executor;
        _binanceClient = binanceClient;
        _logger = logger;
    }

    public async Task<LiveBuyResult> ExecuteHybridEntryAsync(
        string symbol,
        decimal targetUsd,
        BotSettingsEntity settings,
        CancellationToken ct)
    {
        var rules = await _executor.GetRulesAsync(symbol, ct);
        if (rules == null)
            return new LiveBuyResult(false, null, 0, 0, 0, "Rules unavailable");

        targetUsd = QuantizationService.RoundQuoteToPrecision(Math.Max(settings.TargetUsdPerTrade, targetUsd), 2);
        if (targetUsd < settings.MinUsdPerTrade)
            return new LiveBuyResult(false, null, 0, 0, 0, $"Target USD {targetUsd:N2} below minimum {settings.MinUsdPerTrade:N2}");

        // Stage A: LIMIT_MAKER attempt
        if (settings.UseLimitMakerEntry)
        {
            decimal makerPrice = 0m;
            try
            {
                var ticker = await _binanceClient.SpotApi.ExchangeData.GetTickerAsync(symbol, ct);
                if (ticker.Success && ticker.Data != null)
                {
                    var anchorPrice = ticker.Data.BestBidPrice > 0
                        ? ticker.Data.BestBidPrice
                        : (ticker.Data.LastPrice > 0 ? ticker.Data.LastPrice : ticker.Data.BestAskPrice);

                    var discountPct = settings.LimitMakerDiscountBps / 10000m;
                    makerPrice = QuantizationService.RoundPriceToTick(anchorPrice * (1m - discountPct), rules.TickSize);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[LIMIT_MAKER_PRICE_FETCH_ERROR] {Symbol}", symbol);
            }

            _logger.LogDebug("[LIMIT_MAKER_ATTEMPT] {Symbol} price={Price} discount={Discount}bps timeout={Timeout}s",
                symbol, makerPrice, settings.LimitMakerDiscountBps, settings.LimitMakerTimeoutSeconds);

            var limitResult = await _executor.LimitBuyMakerAsync(symbol, targetUsd, makerPrice, ct, settings.LimitMakerTimeoutSeconds);

            if (limitResult.Success && limitResult.ExecutedQty > 0)
            {
                _logger.LogInformation("[LIMIT_MAKER_FILLED] {Symbol} qty={Qty} price={Price}",
                    symbol, limitResult.ExecutedQty, limitResult.AvgPrice);
                return limitResult;
            }

            _logger.LogDebug("[LIMIT_MAKER_TIMEOUT] {Symbol}", symbol);
        }

        // Stage B: Market fallback with SLIPPAGE GUARD
        if (!settings.FallbackToMarketOnEntryTimeout)
        {
            return new LiveBuyResult(false, null, 0, 0, 0, "Market fallback disabled");
        }

        // ✅ CRITICAL FIX 2: CAPTURE EXPECTED PRICE BEFORE MARKET ORDER
        decimal expectedPrice = 0;
        try
        {
            var tickerResult = await _binanceClient.SpotApi.ExchangeData.GetTickerAsync(symbol, ct);
            if (tickerResult.Success && tickerResult.Data != null)
            {
                expectedPrice = tickerResult.Data.BestAskPrice > 0 
                    ? tickerResult.Data.BestAskPrice 
                    : tickerResult.Data.LastPrice;
                
                _logger.LogDebug("[EXPECTED_PRICE] {Symbol} expectedPrice={Price}", symbol, expectedPrice);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[PRICE_FETCH_ERROR] {Symbol}", symbol);
        }

        _logger.LogDebug("[MARKET_FALLBACK] {Symbol}", symbol);
        var marketResult = await _executor.MarketBuyAsync(symbol, targetUsd, ct);

        if (!marketResult.Success)
            return new LiveBuyResult(false, marketResult.OrderId, 0, 0, 0, marketResult.Error ?? "Market buy failed");

        // ✅ CRITICAL FIX 2: SLIPPAGE GUARD + EMERGENCY EXIT
        if (marketResult.ExecutedQty > 0 && expectedPrice > 0 && settings.ExecuteTrades)
        {
            var slippagePct = ((marketResult.AvgPrice - expectedPrice) / expectedPrice) * 100m;

            if (slippagePct > settings.MaxAllowedEntrySlippagePct)
            {
                _logger.LogCritical(
                    "[SLIPPAGE_VIOLATION] {Symbol} slippage={Slippage:F4}% max={Max:F4}% expected={Expected} actual={Actual}",
                    symbol, slippagePct, settings.MaxAllowedEntrySlippagePct, expectedPrice, marketResult.AvgPrice);

                // ✅ EMERGENCY EXIT - IMMEDIATE MARKET SELL
                try
                {
                    _logger.LogError("[EMERGENCY_EXIT_START] {Symbol} qty={Qty}", symbol, marketResult.ExecutedQty);
                    
                    var emergencySell = await _executor.MarketSellAsync(symbol, marketResult.ExecutedQty, ct);
                    
                    _logger.LogCritical(
                        "[EMERGENCY_EXIT_COMPLETE] {Symbol} sellSuccess={Success} qty={Qty}",
                        symbol, emergencySell.Success, marketResult.ExecutedQty);
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "[EMERGENCY_EXIT_FAILED] {Symbol} - MANUAL INTERVENTION REQUIRED", symbol);
                }

                return new LiveBuyResult(
                    false,
                    marketResult.OrderId,
                    0,
                    0,
                    0,
                    $"[SLIPPAGE_VIOLATION] {slippagePct:F2}% exceeded {settings.MaxAllowedEntrySlippagePct:F2}% - emergency exited");
            }
            else if (slippagePct > 0)
            {
                _logger.LogInformation("[SLIPPAGE_OK] {Symbol} slippage={Slippage:F4}%", symbol, slippagePct);
            }
        }

        _logger.LogInformation("[MARKET_FILLED] {Symbol} qty={Qty} price={Price}",
            symbol, marketResult.ExecutedQty, marketResult.AvgPrice);

        return marketResult;
    }
}