using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using static CoinSoul.Trading.Core.ExecutionLockService;

namespace CoinSoul.Trading.Engine;

public sealed class PrecisionTradeExecutor
{
    private readonly CoinSoulDbContext _db;
    private readonly ITradeExecutor _exec;
    private readonly NetProfitTargetService _netProfit;
    private readonly ExecutionGuardService _guard;
    private readonly IAccountTradeWriter _tradeWriter;
    private readonly HybridEntryService _hybridEntry;
    private readonly ILogger<PrecisionTradeExecutor> _logger; // ✅ NEW
    private readonly PortfolioRefreshService _portfolioRefresh; // ✅ NEW

    public PrecisionTradeExecutor(
        CoinSoulDbContext db,
        ITradeExecutor exec,
        NetProfitTargetService netProfit,
        ExecutionGuardService guard,
        IAccountTradeWriter tradeWriter,
        HybridEntryService hybridEntry,
        ILogger<PrecisionTradeExecutor> logger, // ✅ NEW
        PortfolioRefreshService portfolioRefresh) // ✅ NEW
    {
        _db = db;
        _exec = exec;
        _netProfit = netProfit;
        _guard = guard;
        _tradeWriter = tradeWriter;
        _hybridEntry = hybridEntry;
        _logger = logger;
        _portfolioRefresh = portfolioRefresh; // ✅ NEW
    }

    public async Task<ExecuteTradeResult> ExecutePrecisionTradeAsync(
        string symbol,
        decimal tradeSizeUsd,
        BotSettingsEntity settings,
        MarketRegimeDecision regimeDecision,
        Action<string, string>? log,
        CancellationToken ct)
    {
        // ✅ IMPORTANT
        // Do NOT acquire a per-symbol entry lock here.
        // The orchestrator already acquires and holds the lock for the whole tick.
        // Double-locking causes false [LOCK_BUSY] blocks.

        PositionEntity? pos = null;
        try
        {
            // EntryPending is only a reservation. Do NOT mark it as open until we have a filled buy,
            // otherwise a failed/slow entry will consume a slot and cause perpetual MAX_POSITIONS.
            pos = new PositionEntity
            {
                Symbol = symbol,
                Stage = (int)TradeStage.EntryPending,
                IsActive = true,
                IsOpen = false,
                TargetNetProfitUsd = settings.NetProfitTargetUsd,
                OpenedAtUtc = DateTime.UtcNow,
                CreatedAtUtc = DateTime.UtcNow,
                UpdatedAtUtc = DateTime.UtcNow
            };

            _db.Positions.Add(pos);
            await _db.SaveChangesAsync(ct);

            await LogStageEventAsync(pos.Id, symbol, "ENTRY_PENDING", "Created position", ct);

            // Pre-validate symbol trading rules to avoid creating "ghost" positions for invalid/non-tradable symbols.
            var rules = await _exec.GetRulesAsync(symbol, ct);
            if (rules is null)
            {
                pos.Stage = (int)TradeStage.Failed;
                pos.IsActive = false;
                pos.IsOpen = false;
                pos.CloseReason = "RULES_NOT_FOUND";
                pos.LastError = $"No trading rules found for {symbol} (invalid or not tradable).";
                pos.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                await LogStageEventAsync(pos.Id, symbol, "ENTRY_REJECTED", "Rules not found (invalid/not tradable symbol)", ct);
                return new ExecuteTradeResult { Success = false, Error = pos.LastError, PositionId = pos.Id };
            }

            // ✅ FIXED: Now returns LiveBuyResult
            var buy = await _hybridEntry.ExecuteHybridEntryAsync(symbol, tradeSizeUsd, settings, ct);

            if (!buy.Success || buy.ExecutedQty <= 0)
            {
                pos.Stage = (int)TradeStage.Failed;
                pos.IsActive = false;
                pos.IsOpen = false;
                pos.LastError = buy.Error;
                pos.CloseReason = "ENTRY_FAILED";
                pos.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                await LogStageEventAsync(pos.Id, symbol, "ENTRY_FAILED", buy.Error ?? "Unknown", ct);

                return new ExecuteTradeResult { Success = false, Error = buy.Error, PositionId = pos.Id };
            }

            pos.Stage = (int)TradeStage.EntryFilled;
            pos.BuyOrderId = buy.OrderId;
            pos.UpdatedAtUtc = DateTime.UtcNow;
            pos.EntryPrice = buy.AvgPrice;
            pos.Quantity = buy.ExecutedQty;
            pos.QuoteUsedUsdt = buy.QuoteUsed;
            pos.FeesPaidUsd = buy.QuoteUsed * settings.TakerFeeRate;
            pos.IsOpen = true; // now it's a real open position (entry filled)
            await _db.SaveChangesAsync(ct);

            await LogStageEventAsync(pos.Id, symbol, "ENTRY_FILLED", $"Bought {buy.ExecutedQty:0.########} @ {buy.AvgPrice:0.########}", ct);

            // ✅ NEW: Save to AccountTrades table
            await SaveBuyTradeToAccountTradesAsync(symbol, buy, ct);

            // IMPORTANT:
            // Do NOT refresh balances / perform any post-entry capital checks before exit protection is placed.
            // The priority after ENTRY_FILLED is to protect the position immediately with OCO (or fallback orders).
            _logger.LogInformation("[POST_BUY] {Symbol} Qty={Qty} Avg={Avg} QuoteUsed={QuoteUsed}",
                symbol, buy.ExecutedQty, buy.AvgPrice, buy.QuoteUsed);
            log?.Invoke("INFO", $"[POST_BUY] {symbol} Qty={buy.ExecutedQty:0.########} Avg={buy.AvgPrice:0.########}");

            // Continue directly with OCO placement...
            var exitRules = rules;
            if (exitRules == null)
            {
                pos.LastError = "Rules unavailable";
                pos.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return new ExecuteTradeResult { Success = false, Error = "Rules unavailable", PositionId = pos.Id };
            }

            // Calculate TP/SL prices
            var effectiveTpPct = Math.Max(settings.TakeProfitGrossPct * regimeDecision.TpMultiplier, 0.01m);
            var effectiveSlPct = Math.Max(settings.StopLossGrossPct, 0.01m);
            var baseNetTarget = Math.Max(settings.NetProfitTargetUsd * regimeDecision.TpMultiplier, 0m);

            var feeAwarePctTp = NetProfitExitService.ComputeTpFromGrossPercent(
                pos.EntryPrice,
                effectiveTpPct,
                settings);

            var feeAwareNetUsdTp = NetProfitExitService.ComputeTpFromNetUsd(
                pos.EntryPrice,
                pos.Quantity,
                baseNetTarget,
                settings);

            var tpPrice = Math.Max(feeAwarePctTp, feeAwareNetUsdTp);
            var stopPriceRaw = NetProfitExitService.ComputeStopPrice(pos.EntryPrice, effectiveSlPct);
            var stopLimitPriceRaw = NetProfitExitService.ComputeStopLimitPrice(stopPriceRaw, settings.OcoStopLimitBufferPct);

            // Quantize quantity
            var qtyResult = SymbolRulesNormalizer.NormalizeQty(pos.Quantity, exitRules.StepSize, exitRules.MinQty);
            if (!qtyResult.Ok)
            {
                pos.LastError = qtyResult.Why;
                pos.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
                return new ExecuteTradeResult { Success = false, Error = qtyResult.Why, PositionId = pos.Id };
            }

            var normQty = qtyResult.NormalizedQty;

            // ✅ NEW: Place OCO with retry logic
            pos.Stage = (int)TradeStage.OcoPlacing;
            pos.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            var tpPriceForLog = QuantizationService.NormalizePriceForExchange(tpPrice, exitRules.TickSize);
            var stopPriceForLog = QuantizationService.NormalizePriceForExchange(stopPriceRaw, exitRules.TickSize);
            var stopLimitForLog = QuantizationService.NormalizePriceForExchange(stopLimitPriceRaw, exitRules.TickSize);
            _logger.LogInformation("[OCO_CREATE_ATTEMPT] {Symbol} Qty={Qty} TP={TP} Stop={Stop} StopLimit={StopLimit}",
                symbol, normQty, tpPriceForLog, stopPriceForLog, stopLimitForLog);
            await LogStageEventAsync(pos.Id, symbol, "OCO_CREATE_ATTEMPT",
                $"Qty={normQty:0.########} TP={tpPriceForLog:0.########} Stop={stopPriceForLog:0.########} StopLimit={stopLimitForLog:0.########}", ct);

            var ocoResult = await PlaceOcoWithRetryAsync(
                pos,
                symbol,
                normQty,
                tpPrice,
                stopPriceRaw,
                stopLimitPriceRaw,
                exitRules,
                settings,
                log,
                ct);

            if (ocoResult.Success)
            {
                pos.Stage = (int)TradeStage.OcoPlaced;
                pos.OcoOrderId = ocoResult.OrderListId;
                pos.UpdatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);

                await LogStageEventAsync(pos.Id, symbol, "OCO_OK", $"ListId={ocoResult.OrderListId}", ct);
                await TryRefreshPortfolioAfterProtectionAsync(symbol, settings, log, ct);
                return new ExecuteTradeResult { Success = true, PositionId = pos.Id };
            }
            else
            {
                log?.Invoke("WARN", $"[OCO_FAIL_ALL_RETRIES] {symbol}: {ocoResult.Error}");
                _logger.LogWarning("[OCO_FAILED] {Symbol} {Error}", symbol, ocoResult.Error);
                await LogStageEventAsync(pos.Id, symbol, "OCO_FAILED", ocoResult.Error ?? "Unknown OCO failure", ct);

                // Fallback to separate orders
                if (settings.PlaceSeparateTpSlIfOcoFails)
                {
                    await LogStageEventAsync(pos.Id, symbol, "FALLBACK_ORDERS_ATTEMPT", "Placing separate TP/SL orders", ct);
                    await PlaceFallbackOrdersAsync(pos, normQty, tpPrice, stopPriceRaw, exitRules, settings, ct);
                    await TryRefreshPortfolioAfterProtectionAsync(symbol, settings, log, ct);
                    return new ExecuteTradeResult { Success = true, PositionId = pos.Id };
                }
                else
                {
                    pos.CloseReason = "OCO_FAIL_NO_FALLBACK";
                    pos.Stage = (int)TradeStage.OcoPlaced;
                    pos.UpdatedAtUtc = DateTime.UtcNow;
                    await _db.SaveChangesAsync(ct);
                    await LogStageEventAsync(pos.Id, symbol, "OCO_FAIL_NO_FALLBACK", "Fallback disabled", ct);
                    return new ExecuteTradeResult { Success = true, PositionId = pos.Id };
                }
            }
        }
        catch (Exception ex)
        {
            // ✅ Safety: do not leave a stuck EntryPending record that causes MAX_POSITIONS forever.
            _logger.LogError(ex, "[EXECUTOR_EXCEPTION] PrecisionTradeExecutor failed for {Symbol}", symbol);
            log?.Invoke("ERROR", $"[EXECUTOR_EXCEPTION] {symbol}: {ex.Message}");

            if (pos != null)
            {
                try
                {
                    var tracked = await _db.Positions.FirstOrDefaultAsync(x => x.Id == pos.Id, ct);
                    if (tracked != null)
                    {
                        if (tracked.Stage == (int)TradeStage.EntryPending)
                        {
                            tracked.Stage = (int)TradeStage.Failed;
                            tracked.IsActive = false;
                            tracked.IsOpen = false;
                            tracked.CloseReason = "EXECUTOR_EXCEPTION_BEFORE_FILL";
                        }
                        else
                        {
                            // Buy filled but exception happened after fill. Keep OPEN for reconciliation/position manager.
                            tracked.IsActive = true;
                            tracked.IsOpen = true;
                            tracked.CloseReason = "EXECUTOR_EXCEPTION_AFTER_FILL";
                        }

                        tracked.LastError = ex.Message.Length > 350 ? ex.Message[..350] : ex.Message;
                        tracked.UpdatedAtUtc = DateTime.UtcNow;
                        await _db.SaveChangesAsync(ct);
                    }
                }
                catch (Exception inner)
                {
                    _logger.LogError(inner, "[EXECUTOR_EXCEPTION] Failed to persist exception state for {Symbol}", symbol);
                }
            }

            return new ExecuteTradeResult { Success = false, Error = ex.Message, PositionId = pos?.Id ?? 0 };
        }
        finally
        {
            // No lockLease to dispose here; orchestrator manages the lock.
        }
    }

    /// <summary>
    /// Places OCO with intelligent retry logic
    /// </summary>
    private async Task<OcoRetryResult> PlaceOcoWithRetryAsync(
        PositionEntity pos,
        string symbol,
        decimal quantity,
        decimal tpPrice,
        decimal stopPrice,
        decimal stopLimitPrice,
        SymbolTradingRules rules,
        BotSettingsEntity settings,
        Action<string, string>? log,
        CancellationToken ct)
    {
        // ✅ IDEMPOTENCY: Check if OCO already exists
        if (pos.OcoOrderId.HasValue)
        {
            _logger.LogWarning("[OCO_IDEMPOTENCY] {Symbol} - OCO already placed: {OrderId}", symbol, pos.OcoOrderId);
            return new OcoRetryResult
            {
                Success = true,
                OrderListId = pos.OcoOrderId.Value,
                Error = null
            };
        }

        var maxAttempts = Math.Max(1, settings.OcoRetryAttempts);

        string? lastError = null;
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                // Re-quantize on each attempt
                var normQty = QuantizationService.NormalizeQtyForExchange(quantity, rules.StepSize);
                var normTpPrice = QuantizationService.NormalizePriceForExchange(tpPrice, rules.TickSize);
                var normStopPrice = QuantizationService.NormalizePriceForExchange(stopPrice, rules.TickSize);

                // ✅ ADAPTIVE BUFFER: Adjust stopLimit on retries
                var bufferTicks = Math.Max(3m, 3m + (attempt - 1) * 3m);
                var adjustedStopLimit = normStopPrice - (rules.TickSize * Math.Max(2m, bufferTicks));
                var normStopLimitPrice = QuantizationService.NormalizePriceForExchange(adjustedStopLimit, rules.TickSize);

                // ✅ SAFETY: enforce strict stopLimit < stop after normalization
                while (normStopLimitPrice >= normStopPrice)
                {
                    adjustedStopLimit -= rules.TickSize;
                    normStopLimitPrice = QuantizationService.NormalizePriceForExchange(adjustedStopLimit, rules.TickSize);
                }

                if (normStopLimitPrice <= 0)
                    throw new InvalidOperationException($"Invalid OCO stopLimit after normalization for {symbol}");

                // ✅ SAFETY: Ensure TP > entry
                if (normTpPrice <= pos.EntryPrice)
                {
                    normTpPrice = pos.EntryPrice + (rules.TickSize * 3);
                }

                // ✅ SAFETY: Ensure stop < entry
                if (normStopPrice >= pos.EntryPrice)
                {
                    normStopPrice = pos.EntryPrice - (rules.TickSize * 3);
                }

                // ✅ Validate MinNotional
                var notionalCheck = QuantizationService.ValidateMinNotional(normTpPrice, normQty, rules.MinNotional);
                if (!notionalCheck.Valid)
                {
                    _logger.LogWarning("[OCO_NOTIONAL_FAIL] {Symbol} attempt {Attempt}: {Reason}",
                        symbol, attempt, notionalCheck.Reason);

                    await LogTradingEventAsync("OCO_RETRY", symbol,
                        $"Attempt {attempt}/{maxAttempts}: {notionalCheck.Reason}", pos.Id, ct);

                    continue; // Skip to next attempt
                }

                // ✅ Validate price relationships
                var validation = NetProfitExitService.ValidateOcoPrices(
                    pos.EntryPrice,
                    normTpPrice,
                    normStopPrice,
                    normStopLimitPrice);

                if (!validation.Valid)
                {
                    _logger.LogWarning("[OCO_VALIDATION_FAIL] {Symbol} attempt {Attempt}: {Reason}",
                        symbol, attempt, validation.Reason);

                    await LogTradingEventAsync("OCO_RETRY", symbol,
                        $"Attempt {attempt}/{maxAttempts}: {validation.Reason}", pos.Id, ct);

                    continue;
                }

                // ✅ PLACE OCO
                _logger.LogInformation("[OCO_ATTEMPT] {Symbol} attempt {Attempt}/{MaxAttempts} TP={TP} Stop={Stop} StopLimit={StopLimit}",
                    symbol, attempt, maxAttempts, normTpPrice, normStopPrice, normStopLimitPrice);

                var ocoResult = await _exec.PlaceOcoSellAsync(
                    symbol,
                    normQty,
                    normTpPrice,
                    normStopPrice,
                    normStopLimitPrice,
                    ct);

                if (ocoResult.Success)
                {
                    _logger.LogInformation("[OCO_SUCCESS] {Symbol} on attempt {Attempt}: OrderListId={OrderListId}",
                        symbol, attempt, ocoResult.OrderListId);

                    await LogTradingEventAsync("OCO_OK", symbol,
                        $"Placed on attempt {attempt}: OrderListId={ocoResult.OrderListId}", pos.Id, ct);

                    return new OcoRetryResult
                    {
                        Success = true,
                        OrderListId = ocoResult.OrderListId,
                        Error = null
                    };
                }
                else
                {
                    lastError = ocoResult.Error;
                    _logger.LogWarning("[OCO_RETRY] {Symbol} attempt {Attempt}/{MaxAttempts} failed: {Error}",
                        symbol, attempt, maxAttempts, ocoResult.Error);

                    await LogTradingEventAsync("OCO_RETRY", symbol,
                        $"Attempt {attempt}/{maxAttempts} failed: {ocoResult.Error}", pos.Id, ct);

                    // If last attempt, don't sleep
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(500 * attempt, ct); // Progressive backoff
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[OCO_EXCEPTION] {Symbol} attempt {Attempt}: {Message}",
                    symbol, attempt, ex.Message);

                await LogTradingEventAsync("OCO_RETRY", symbol,
                    $"Attempt {attempt}/{maxAttempts} exception: {ex.Message}", pos.Id, ct);

                if (attempt >= maxAttempts)
                {
                    return new OcoRetryResult
                    {
                        Success = false,
                        OrderListId = null,
                        Error = $"All {maxAttempts} attempts failed: {ex.Message}"
                    };
                }
            }
        }

        // All attempts failed
        _logger.LogError("[OCO_FAIL] {Symbol} - all {MaxAttempts} attempts exhausted", symbol, maxAttempts);

        await LogTradingEventAsync("OCO_FAIL", symbol,
            $"All {maxAttempts} retry attempts failed", pos.Id, ct);

        return new OcoRetryResult
        {
            Success = false,
            OrderListId = null,
            Error = lastError is null ? $"OCO failed after {maxAttempts} attempts" : $"OCO failed after {maxAttempts} attempts: {lastError}"
        };
    }

    /// <summary>
    /// Fallback after OCO failure.
    /// IMPORTANT: do not place two independent sell orders for the full quantity,
    /// because Binance can reject the second reservation with insufficient balance.
    /// Instead, arm local-managed exit and let the position manager close the trade.
    /// </summary>
    private async Task PlaceFallbackOrdersAsync(
        PositionEntity pos,
        decimal qty,
        decimal tpPrice,
        decimal stopPrice,
        SymbolTradingRules rules,
        BotSettingsEntity settings,
        CancellationToken ct)
    {
        _logger.LogWarning("[OCO_FALLBACK_LOCAL] {Symbol} - arming local-managed exit instead of separate TP/SL orders", pos.Symbol);

        pos.TakeProfitOrderId = null;
        pos.StopLossOrderId = null;
        pos.CloseReason = "LOCAL_EXIT_ARMED";
        pos.Stage = (int)TradeStage.OcoPlaced;
        pos.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        await LogStageEventAsync(pos.Id, pos.Symbol, "LOCAL_EXIT_ARMED",
            "OCO failed; armed local-managed exit (no dual exchange sell reservation)", ct);
        await LogTradingEventAsync("OCO_FALLBACK", pos.Symbol,
            "OCO failed; local-managed exit armed instead of separate TP/SL orders", pos.Id, ct);
    }

    private async Task SaveBuyTradeToAccountTradesAsync(
        string symbol,
        LiveBuyResult buyResult,
        CancellationToken ct)
    {
        try
        {
            var tradeId = GenerateTradeId(buyResult.OrderId ?? 0, DateTime.UtcNow);

            var accountTrade = new AccountTradeEntity
            {
                TradeId = tradeId,
                Symbol = symbol,
                Side = "BUY",
                Price = buyResult.AvgPrice,
                Quantity = buyResult.ExecutedQty,
                QuoteQty = buyResult.QuoteUsed,
                Commission = 0m,
                CommissionAsset = "UNKNOWN",
                IsMaker = false,
                TradeTimeUtc = DateTime.UtcNow,
                Source = "BOT",
                OrderId = buyResult.OrderId
            };

            await _tradeWriter.SaveAsync(accountTrade, ct);
        }
        catch (Exception ex)
        {
            await LogStageEventAsync(0, symbol, "ACCOUNT_TRADE_SAVE_ERROR",
                $"Failed to save BUY trade: {ex.Message}", ct);
        }
    }

    private static long GenerateTradeId(long orderId, DateTime timestamp)
    {
        var timestampPart = (timestamp.Ticks / 10000000) & 0x7FF;
        return (orderId << 11) | timestampPart;
    }

    private async Task LogStageEventAsync(int positionId, string symbol, string type, string message, CancellationToken ct)
    {
        _db.Events.Add(new EventEntity
        {
            Level = "INFO",
            Type = type,
            Message = message,
            PositionId = positionId,
            Symbol = symbol,
            AtUtc = DateTime.UtcNow
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task LogTradingEventAsync(string type, string symbol, string message, int? positionId, CancellationToken ct)
    {
        try
        {
            _db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = type == "OCO_FAIL" ? "ERROR" : type == "OCO_RETRY" ? "WARN" : "INFO",
                Type = type,
                Symbol = symbol,
                Message = message,
                CorrelationId = positionId?.ToString()
            });

            await _db.SaveChangesAsync(ct);
        }
        catch { }
    }
    private async Task TryRefreshPortfolioAfterProtectionAsync(
        string symbol,
        BotSettingsEntity settings,
        Action<string, string>? log,
        CancellationToken ct)
    {
        try
        {
            await _portfolioRefresh.RefreshAsync(settings,false,ct);
            _logger.LogInformation("[BALANCE_REFRESH_AFTER_PROTECTION] {Symbol}", symbol);
            log?.Invoke("INFO", $"[BALANCE_REFRESH_AFTER_PROTECTION] {symbol}");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[BALANCE_REFRESH_FAILED] {Symbol}", symbol);
            log?.Invoke("WARN", $"[BALANCE_REFRESH_FAILED] {symbol}: {ex.Message}");
        }
    }

}

/// <summary>
/// Result from OCO retry attempts
/// </summary>
public sealed record OcoRetryResult
{
    public bool Success { get; init; }
    public long? OrderListId { get; init; }
    public string? Error { get; init; }
}

public sealed class ExecuteTradeResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int PositionId { get; set; }
}