using System.Text.RegularExpressions;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;
using CoinSoul.Trading.Engine.V2;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using CoinSoul.Trading.Engine.Validation; // ADD at top

namespace CoinSoul.Trading.Application;

/// <summary>
/// Production-grade symbol firewall. Prevents invalid symbols from corrupting
/// queue operations, WebSocket subscriptions, and order placement.
/// Thread-safe, stateless, high-performance validation.
/// </summary>
public sealed partial class SymbolSanitizer
{
    private readonly ILogger<SymbolSanitizer> _logger;
    private static readonly HashSet<string> ValidQuoteAssets = new() { "USDT", "BUSD", "USDC", "BTC", "ETH" };

    [GeneratedRegex(@"^[A-Z0-9]{2,15}(USDT|BUSD|USDC|BTC|ETH)$", RegexOptions.Compiled)]
    private static partial Regex ValidSymbolPattern();

    public SymbolSanitizer(ILogger<SymbolSanitizer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates and normalizes a single symbol. Returns null if invalid.
    /// </summary>
    public string? TryNormalizeSymbol(string? rawSymbol, string? correlationId = null)
    {
        if (string.IsNullOrWhiteSpace(rawSymbol))
        {
            LogRejection(rawSymbol, null, "NULL_OR_EMPTY", correlationId);
            return null;
        }

        var cleaned = rawSymbol.Trim().ToUpperInvariant();

        // Reject Unicode replacement characters (?) or question marks
        if (cleaned.Contains('?') || cleaned.Contains('\uFFFD'))
        {
            LogRejection(rawSymbol, cleaned, "CONTAINS_UNICODE_REPLACEMENT", correlationId);
            return null;
        }

        // Remove all non-alphanumeric characters
        cleaned = Regex.Replace(cleaned, @"[^A-Z0-9]", "");

        // Validate length
        if (cleaned.Length < 5 || cleaned.Length > 20)
        {
            LogRejection(rawSymbol, cleaned, "INVALID_LENGTH", correlationId);
            return null;
        }

        // Validate format (base asset + valid quote)
        if (!ValidSymbolPattern().IsMatch(cleaned))
        {
            LogRejection(rawSymbol, cleaned, "INVALID_FORMAT", correlationId);
            return null;
        }

        if (rawSymbol != cleaned)
        {
            _logger.LogDebug("[SYMBOL_NORMALIZED] Raw=\"{Raw}\" ? Clean=\"{Clean}\" | Correlation={Correlation}",
                rawSymbol, cleaned, correlationId ?? "N/A");
        }

        return cleaned;
    }

    /// <summary>
    /// Validates and deduplicates a list of symbols. Returns only valid ones.
    /// </summary>
    public List<string> ValidateAndCleanSymbols(List<string>? rawSymbols, string? correlationId = null)
    {
        if (rawSymbols == null || rawSymbols.Count == 0)
        {
            _logger.LogWarning("[SYMBOL_LIST_EMPTY] No symbols provided | Correlation={Correlation}",
                correlationId ?? "N/A");
            return new List<string>();
        }

        var validSymbols = new HashSet<string>(StringComparer.Ordinal);
        var rejectedCount = 0;

        foreach (var raw in rawSymbols)
        {
            var normalized = TryNormalizeSymbol(raw, correlationId);
            if (normalized != null)
            {
                validSymbols.Add(normalized);
            }
            else
            {
                rejectedCount++;
            }
        }

        _logger.LogInformation(
            "[SYMBOL_VALIDATION_COMPLETE] Input={Input} Valid={Valid} Rejected={Rejected} Deduplicated={Dedup} | Correlation={Correlation}",
            rawSymbols.Count,
            validSymbols.Count,
            rejectedCount,
            rawSymbols.Count - rejectedCount - validSymbols.Count,
            correlationId ?? "N/A");

        return validSymbols.ToList();
    }

    private void LogRejection(string? raw, string? clean, string reason, string? correlationId)
    {
        _logger.LogWarning(
            "[SYMBOL_REJECTED] Raw=\"{Raw}\" Clean=\"{Clean}\" Reason={Reason} | Correlation={Correlation}",
            raw ?? "NULL",
            clean ?? "NULL",
            reason,
            correlationId ?? "N/A");
    }
}

// ====================================================================
// SYMBOL VALIDATION - Singleton (stateless, thread-safe)
// ====================================================================
builder.Services.AddSingleton<SymbolSanitizer>();

// ====================================================================
// INFRASTRUCTURE - Singleton Services
// ====================================================================
builder.Services.AddSingleton<BinanceApplicationService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IExecutionModeDecider, ExecutionModeDecider>();
builder.Services.AddMemoryCache(); // Required by TradingSafetyGate

// ====================================================================
// WEBSOCKET STREAMING - Singleton (thread-safe, shared state)
 // ====================================================================
builder.Services.AddSingleton<IMarketStreamService, MarketStreamService>();
builder.Services.Configure<MarketStreamOptions>(builder.Configuration.GetSection("MarketStream"));

// ====================================================================
// MARKET DATA CACHE - Singleton (thread-safe, shared cache)
// ====================================================================
builder.Services.Configure<MarketDataCacheOptions>(
    builder.Configuration.GetSection(MarketDataCacheOptions.SectionName));
builder.Services.AddSingleton<IMarketDataCache, CoinSoul.Trading.Engine.Cache.MarketDataCache>();

// ====================================================================
// SYMBOL PROVIDERS - Singleton (static data)
// ====================================================================
builder.Services.AddSingleton<BinanceSymbolProvider>();
builder.Services.AddSingleton<ISymbolProvider, CachedSymbolProvider>();
builder.Services.AddSingleton<IMarketDataProvider, BinanceMarketDataProvider>();

// ====================================================================
// ADAPTIVE SCANNING - Singleton (stateful scheduler)
// ====================================================================
builder.Services.AddSingleton<IScanScheduler, AdaptiveScanScheduler>();
builder.Services.Configure<AdaptiveScanOptions>(builder.Configuration.GetSection("AdaptiveScan"));

// ====================================================================
// SCOPED SERVICES - Database & Trading Logic
// ====================================================================

// Core Trading Services (depend on DbContext = Scoped)
builder.Services.AddScoped<IMarketKlineProvider, MarketKlineProvider>();
builder.Services.AddScoped<ITradeExecutor, BinanceTradeExecutor>();
builder.Services.AddScoped<ISymbolValidator, BinanceSymbolValidator>();
builder.Services.AddScoped<IBestSymbolsService, BestSymbolsService>();
builder.Services.AddScoped<INotificationService, TradingNotificationService>();
builder.Services.AddScoped<IPortfolioService, BinancePortfolioService>();
builder.Services.AddScoped<IAccountTradeWriter, AccountTradeWriter>();

// Essential Services
builder.Services.AddScoped<HybridEntryService>();
builder.Services.AddScoped<PortfolioRefreshService>();
builder.Services.AddScoped<QuantizationService>();
builder.Services.AddScoped<NetProfitExitService>();
builder.Services.AddScoped<CoinSoul.Trading.Engine.Settings.BotSettingsService>();

// Strategies
builder.Services.AddScoped<ManualStrategyA>();
builder.Services.AddScoped<ScalperStrategyD>();
builder.Services.AddScoped<AutoScalperStrategy>();
builder.Services.AddScoped<ITradingStrategy, ManualStrategyA>();
builder.Services.AddScoped<ITradingStrategy, ScalperStrategyD>();

// Trading Engine
builder.Services.AddScoped<TradingEngine>();
builder.Services.AddScoped<ITradingEngine>(sp => sp.GetRequiredService<TradingEngine>());

// Engine Components (all depend on DbContext)
builder.Services.AddScoped<AutoScalperPositionManager>();
builder.Services.AddScoped<SymbolQueueManager>();
builder.Services.AddScoped<OpportunityDetector>();
builder.Services.AddScoped<RiskGuardService>();
builder.Services.AddScoped<ExecutionGuardService>();
builder.Services.AddScoped<SlippageProtection>();
builder.Services.AddScoped<NetProfitTargetService>();
builder.Services.AddScoped<PrecisionTradeExecutor>();
builder.Services.AddScoped<PositionGuardService>();
builder.Services.AddScoped<CapitalAllocationService>();
builder.Services.AddScoped<SmartCooldownService>();
builder.Services.AddScoped<MarketRegimeService>();
builder.Services.AddScoped<PortfolioStateService>();
builder.Services.AddScoped<IVolatilityCalculator, VolatilityCalculator>();

// Application Layer
builder.Services.AddScoped<ISettingsProvider, DbSettingsProvider>();
builder.Services.AddScoped<ITickLogger, TickLogger>();
builder.Services.AddScoped<AutoScalperOrchestrator>();

// V2 Pipeline Components
builder.Services.AddScoped<GuardEngine>();
builder.Services.AddScoped<ITickPipeline, AutoScalperTickPipeline>();
builder.Services.AddScoped<ExecutionPreconditionsValidator>();
builder.Services.AddScoped<RegimeChangeDetector>();

// Safety Services
builder.Services.AddScoped<ITradingSafetyGate, TradingSafetyGate>();
builder.Services.AddScoped<ExecutionLockService>();

// Analytics
builder.Services.AddScoped<AnalyticsService>();
builder.Services.AddScoped<EquityBaselineService>();

// ? OBSERVABILITY - Scoped (writes to DB)
builder.Services.AddScoped<IEventWriter, DbEventWriter>();
builder.Services.AddScoped<TickEventGuard>();

// ====================================================================
// BINANCE API SERVICES - Scoped (HTTP calls per request)
// ====================================================================
builder.Services.AddHttpClient<ISpotTradeService, SpotTradeService>();
builder.Services.AddScoped<IAutoSpotTradeService, AutoSpotTradeService>();
builder.Services.AddScoped<IAutoAccountDataService, AutoAccountDataService>();

// ====================================================================
// BACKGROUND SERVICES - Hosted Singletons (use IServiceScopeFactory)
// ====================================================================
builder.Services.AddHostedService<TradingWorker>();
builder.Services.AddHostedService<EquityTrackingService>();
builder.Services.AddHostedService<BinanceTradeSyncService>();
builder.Services.AddHostedService<MarketScannerService>();
builder.Services.AddHostedService<StreamSubscriptionManager>();
builder.Services.AddHostedService<PositionReconciliationService>();
builder.Services.AddHostedService<BotSettingsValidationService>();

public async Task EvaluateAsync(BotState state, CancellationToken ct)
{
    var correlationId = Guid.NewGuid().ToString("N")[..8];
    var tickStart = DateTime.UtcNow;
    var tickStage = "Start";

    try
    {
        _logger.LogCritical("? [AUTOSCALPER_ENTERED] Correlation={Correlation}, Diagnostic={Diag}",
            correlationId, _enableDiagnosticLogging);

        // ? PHASE 1: LOAD SETTINGS (< 5ms with AsNoTracking)
        var settingsSnapshot = await _db.BotSettings.AsNoTracking().FirstOrDefaultAsync(ct);
        
        if (settingsSnapshot == null)
        {
            tickStage = "LoadSettings";
            _logger.LogError("[STRATEGY_BLOCK] Reason=BotSettings not found, Correlation={Correlation}",
                correlationId);
            
            EmitTickSummary(correlationId, tickStart, tickStage, false, "SETTINGS_MISSING", null);
            return;
        }

        tickStage = "ValidateGuards";

        // ? PHASE 2: GUARD VALIDATION (< 20ms)
        var guardResult = await _preconditionsValidator.ValidateExecutionPreconditionsAsync(
            settingsSnapshot, state, ct);

        if (!guardResult.Allowed)
        {
            _logger.LogWarning("[GUARD_BLOCK] Code={Code}, Correlation={Correlation}",
                guardResult.Code, correlationId);
            
            EmitTickSummary(correlationId, tickStart, tickStage, false, guardResult.Code, null);
            return;
        }

        // Guard 3: BotStatus
        if (state.Status != BotStatus.Running)
        {
            return GuardResult.Block("BOT_NOT_RUNNING", ...);
        }

        // ... rest of method unchanged ...
            return GuardResult.Block("BOT_NOT_RUNNING", reason, 
                new { 
                    status = state.Status.ToString(),
                    isRunning = state.IsRunning,
                    tradingEnabled = settingsSnapshot.TradingEnabled,
                    killSwitch = settingsSnapshot.KillSwitch,
                    autoScalperEnabled = settingsSnapshot.AutoScalperEnabled
                });
        }

        // Guard 1: StopUntilUtc (RISK_STOP takes precedence)
        if (settingsSnapshot.StopUntilUtc.HasValue && settingsSnapshot.StopUntilUtc.Value > now)
        {
            return GuardResult.Block("RISK_STOP",
                $"Trading blocked until {settingsSnapshot.StopUntilUtc:yyyy-MM-dd HH:mm:ss} UTC",
                new { stopUntil = settingsSnapshot.StopUntilUtc });
        }

        // Guard 2: PauseUntilUtc
        if (settingsSnapshot.PauseUntilUtc.HasValue && settingsSnapshot.PauseUntilUtc.Value > now)
        {
            return GuardResult.Block("RISK_PAUSE",
                $"Trading paused until {settingsSnapshot.PauseUntilUtc:yyyy-MM-dd HH:mm:ss} UTC",
                new { pauseUntil = settingsSnapshot.PauseUntilUtc });
        }

        // ? REMOVED: Redundant checks for TradingEnabled/KillSwitch/AutoScalperEnabled
        // These are already validated by state.IsRunning check above

        // Guard 3: Config validation
        if (settingsSnapshot.TargetUsdPerTrade <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"TargetUsdPerTrade={settingsSnapshot.TargetUsdPerTrade} (must be > 0)",
                new { targetUsd = settingsSnapshot.TargetUsdPerTrade });
        }

        if (settingsSnapshot.MinUsdPerTrade <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"MinUsdPerTrade={settingsSnapshot.MinUsdPerTrade} (must be > 0)",
                new { minUsd = settingsSnapshot.MinUsdPerTrade });
        }

        if (settingsSnapshot.TakeProfitGrossPct <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"TakeProfitGrossPct={settingsSnapshot.TakeProfitGrossPct} (must be > 0)",
                new { tp = settingsSnapshot.TakeProfitGrossPct });
        }

        if (settingsSnapshot.StopLossGrossPct <= 0)
        {
            return GuardResult.Block("INVALID_CONFIG",
                $"StopLossGrossPct={settingsSnapshot.StopLossGrossPct} (must be > 0)",
                new { sl = settingsSnapshot.StopLossGrossPct });
        }

        // Guard 4: Max positions
        var openCount = await _db.Positions.CountAsync(p => p.IsOpen, ct);
        if (settingsSnapshot.MaxOpenTrades > 0 && openCount >= settingsSnapshot.MaxOpenTrades)
        {
            return GuardResult.Block("MAX_POSITIONS_REACHED",
                $"Current={openCount}, Max={settingsSnapshot.MaxOpenTrades}",
                new { current = openCount, max = settingsSnapshot.MaxOpenTrades });
        }

        // Guard 5: Balance
        var portfolio = await _portfolio.GetPortfolioAsync(ct);
        var minRequired = settingsSnapshot.MinUsdPerTrade;
        
        if (portfolio.FreeUsdt < minRequired)
        {
            return GuardResult.Block("BALANCE_TOO_LOW",
                $"Free=${portfolio.FreeUsdt:N2} < Min=${minRequired:N2}",
                new { free = portfolio.FreeUsdt, required = minRequired });
        }

        // ? SUCCESS: All guards passed
        return GuardResult.Allow();
    }
}

public void Enqueue(QueuedSymbol symbol)
{
    if (string.IsNullOrWhiteSpace(symbol.Symbol)) return;

    // ? CRITICAL FIX: Validate symbol before enqueue
    var normalized = _symbolSanitizer.TryNormalizeSymbol(symbol.Symbol);
    
    if (normalized == null)
    {
        _logger.LogWarning("[ENQUEUE_REJECTED] Invalid symbol=\"{Symbol}\"",
            symbol.Symbol);
        return; // Silently skip invalid symbols
    }

    // Use normalized symbol
    var validSymbol = symbol with { Symbol = normalized };

    if (IsInCooldown(normalized)) return;
    if (_queue.Count >= MaxQueueSize) return;

    if (_queue.TryAdd(validSymbol))
    {
        if (_enableDiagnosticLogging)
        {
            _logger.LogDebug("[QUEUE_ADD] {Symbol}, Score={Score:F1}, Count={Count}",
                normalized, validSymbol.Score, _queue.Count);
        }
    }
}

public Task<QueuedSymbol?> DequeueAsync(BotSettings settings, Action<string> log, CancellationToken ct)
{
    if (_enableDiagnosticLogging)
    {
        _logger.LogWarning("[DIAG_DEQUEUE_START] QueueCount={Count}", _queue.Count);
    }

    if (TryDequeue(out var q, null) && !string.IsNullOrWhiteSpace(q.Symbol))
    {
        // ? CRITICAL FIX: Validate symbol at dequeue (defense in depth)
        var normalized = _symbolSanitizer.TryNormalizeSymbol(q.Symbol);
        
        if (normalized == null)
        {
            _logger.LogWarning("[DEQUEUE_REJECTED] Invalid symbol=\"{Symbol}\" - skipping",
                q.Symbol);
            
            // Try next symbol recursively
            return DequeueAsync(settings, log, ct);
        }

        // Use normalized symbol
        var validSymbol = q with { Symbol = normalized };

        if (_enableDiagnosticLogging)
        {
            _logger.LogWarning("[DIAG_DEQUEUE_SUCCESS] Symbol={Symbol}, Score={Score:F1}",
                normalized, validSymbol.Score);
        }
        
        log($"[QUEUE_PULL] {normalized} score={validSymbol.Score:F1}");
        return Task.FromResult<QueuedSymbol?>(validSymbol);
    }

    if (_enableDiagnosticLogging)
    {
        _logger.LogWarning("[DIAG_DEQUEUE_EMPTY] Queue is empty");
    }

    return Task.FromResult<QueuedSymbol?>(null);
}

// ====================================================================
// WEBSOCKET STREAMING - Singleton (thread-safe, shared state)
// ====================================================================
public class MarketStreamService : IMarketStreamService
{
    private readonly IBinanceSocketClient _socketClient;
    private readonly ILogger<MarketStreamService> _logger;
    private readonly IClock _clock;
    private readonly SymbolSanitizer _symbolSanitizer;
    private MarketStreamOptions _options;

    private CancellationTokenSource _cts = new();
    private Task? _workerTask;

    // Active subscriptions
    private readonly ConcurrentDictionary<string, BookTickerSubscription> _bookTickerSubscriptions = new();
    private readonly SemaphoreSlim _subscriptionLock = new(1, 1);

    public MarketStreamService(
        IBinanceSocketClient socketClient,
        ILogger<MarketStreamService> logger,
        IClock clock,
        SymbolSanitizer symbolSanitizer, // ? ADD
        IConfiguration configuration)
    {
        _socketClient = socketClient;
        _logger = logger;
        _clock = clock;
        _symbolSanitizer = symbolSanitizer; // ? ADD
        
        _options = new MarketStreamOptions();
        configuration.GetSection("MarketStream").Bind(_options);
    }

    public async Task SubscribeSymbolsAsync(IEnumerable<string> symbols, CancellationToken ct)
    {
        if (!_isRunning)
        {
            _logger.LogWarning("[WS_SUB] Not running, skipping subscription");
            return;
        }

        var rawSymbols = symbols.ToList();
        
        if (rawSymbols.Count == 0)
        {
            _logger.LogDebug("[WS_SUB] Empty symbol list, skipping");
            return;
        }

        // ? CRITICAL FIX: Validate and clean symbols before subscription
        var validSymbols = _symbolSanitizer.ValidateAndCleanSymbols(rawSymbols);

        if (validSymbols.Count == 0)
        {
            _logger.LogError("[WS_SUB_BLOCKED] All {Count} symbols rejected - cannot subscribe",
                rawSymbols.Count);
            return; // Do NOT throw - just skip subscription
        }

        _logger.LogInformation("[WS_SUB] Validated {Valid}/{Total} symbols for subscription",
            validSymbols.Count, rawSymbols.Count);

        await _subscriptionLock.WaitAsync(ct);
        
        try
        {
            // Unsubscribe from previous bookTicker stream
            if (_bookTickerSubscription != null)
            {
                await _bookTickerSubscription.CloseAsync();
                _bookTickerSubscription = null;
            }

            // Subscribe to bookTicker for validated symbols only
            var subscribeResult = await _socketClient.SpotApi.ExchangeData.SubscribeToBookTickerUpdatesAsync(
                validSymbols,
                data =>
                {
                    _lastDataReceivedUtc = _clock.UtcNow;
                    
                    var ticker = new StreamedBookTicker(
                        data.Data.Symbol,
                        data.Data.BestBidPrice,
                        data.Data.BestBidQuantity,
                        data.Data.BestAskPrice,
                        data.Data.BestAskQuantity,
                        _clock.UtcNow);

                    _bookTickers[data.Data.Symbol] = ticker;

                    if (_options.EnableDebugLogs)
                    {
                        _logger.LogDebug("[WS_DATA] BookTicker {Symbol} Bid={Bid} Ask={Ask}",
                            data.Data.Symbol, data.Data.BestBidPrice, data.Data.BestAskPrice);
                    }
                },
                ct);

            if (!subscribeResult.Success)
            {
                _logger.LogError("[WS_SUB_FAIL] Failed to subscribe to bookTickers: {Error}",
                    subscribeResult.Error?.Message ?? "Unknown");
                return;
            }

            _bookTickerSubscription = subscribeResult.Data;

            // Setup reconnection handler
            _bookTickerSubscription.ConnectionLost += async () =>
            {
                _logger.LogWarning("[WS] BookTicker connection lost, attempting reconnect...");
                _reconnectCount++;
                await Task.Delay(_options.ReconnectDelayMs);
            };

            _bookTickerSubscription.ConnectionRestored += (timeOffline) =>
            {
                _logger.LogInformation("[WS] BookTicker connection restored after {Offline}ms", timeOffline);
            };

            _logger.LogInformation("[WS_SUB_SUCCESS] Subscribed to {Count} symbols for bookTicker",
                validSymbols.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[WS_SUB_EXCEPTION] Error subscribing to symbols");
        }
        finally
        {
            _subscriptionLock.Release();
        }
    }
}

// ====================================================================
// STREAM SUBSCRIPTION MANAGER - Hosted Service
// ====================================================================
public class StreamSubscriptionManager : BackgroundService
{
    private readonly IMarketStreamService _streamService;
    private readonly IMarketDataCache _cache;
    private readonly ILogger<StreamSubscriptionManager> _logger;
    private readonly SymbolSanitizer _symbolSanitizer; // ADD field

    private readonly bool _useWebSocketMarketData;
    private readonly int _refreshIntervalSeconds;
    private readonly int _maxSubscribedSymbols;

    // Timer for regular subscription refresh
    private PeriodicTimer? _timer;

    public StreamSubscriptionManager(
        IMarketStreamService streamService,
        IMarketDataCache cache,
        ILogger<StreamSubscriptionManager> logger,
        SymbolSanitizer symbolSanitizer, // ? ADD
        IConfiguration configuration)
    {
        _streamService = streamService;
        _cache = cache;
        _logger = logger;
        _symbolSanitizer = symbolSanitizer; // ? ADD
        
        _useWebSocketMarketData = configuration.GetValue<bool>("UseWebSocketMarketData", false);
        _refreshIntervalSeconds = configuration.GetValue<int>("MarketStream:SubscriptionRefreshIntervalSeconds", 60);
        _maxSubscribedSymbols = configuration.GetValue<int>("MarketStream:MaxSubscribedSymbols", 50);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("[WS_MGR] Starting stream subscription manager");

        // Initial subscription
        await RefreshSubscriptionsAsync(stoppingToken);

        // Periodic refresh
        _timer = new PeriodicTimer(TimeSpan.FromSeconds(_refreshIntervalSeconds));
        
        while (await _timer.WaitForTickAsync(stoppingToken))
        {
            await RefreshSubscriptionsAsync(stoppingToken);
        }
    }

    private async Task RefreshSubscriptionsAsync(CancellationToken ct)
    {
        // Get top symbols by volume from 24h tickers
        var allTickers = _streamService.GetAll24hTickers();
        
        if (allTickers.Count == 0)
        {
            _logger.LogWarning("[WS_MGR] No tickers available for subscription refresh");
            return;
        }

        var topSymbols = allTickers
            .Where(t => t.Symbol.EndsWith("USDT", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(t => t.QuoteVolume)
            .Take(_maxSubscribedSymbols)
            .Select(t => t.Symbol)
            .ToList();

        if (topSymbols.Count == 0)
        {
            _logger.LogWarning("[WS_MGR] No valid symbols for subscription");
            return;
        }

        // ? CRITICAL FIX: Validate symbols before passing to stream service
        var validSymbols = _symbolSanitizer.ValidateAndCleanSymbols(topSymbols);

        if (validSymbols.Count == 0)
        {
            _logger.LogError("[WS_MGR_BLOCKED] All {Count} symbols rejected after validation",
                topSymbols.Count);
            return;
        }

        _logger.LogInformation("[WS_MGR] Refreshing subscriptions with {Valid}/{Total} symbols",
            validSymbols.Count, topSymbols.Count);

        await _streamService.SubscribeSymbolsAsync(validSymbols, ct);

        var stats = _streamService.GetStats();
        _logger.LogInformation(
            "[WS_MGR] Subscription refresh complete | " +
            "Subscribed={Sub}, BookTickers={Book}, Tickers24h={Tickers}, Healthy={Healthy}",
            stats.SubscribedSymbolCount,
            stats.BookTickerCount,
            stats.Ticker24hCount,
            _streamService.IsHealthy);
    }
}

// ====================================================================
// BOT STATE - Represents the in-memory state of a trading bot
// ====================================================================
public sealed class BotState
{
    private readonly LinkedList<BotLogEntry> _logs = new();
    private readonly object _logLock = new();

    public BotStatus Status { get; private set; } = BotStatus.Stopped;
    
    // ? CRITICAL FIX: IsRunning is DERIVED from Status (single source of truth)
    public bool IsRunning => Status == BotStatus.Running;
    
    public string? LastError { get; private set; }
    public DateTimeOffset? LastTickAt { get; private set; }

    public BotSettings Settings { get; private set; } = new();
    public BotMarketSnapshot Market { get; private set; } = new();

    public DateTime StartedAt { get; private set; }
    public int TickCount { get; private set; }

    public List<PaperPosition> OpenPositions { get; } = new();
    public List<TradeHistoryItem> TradeHistory { get; } = new();
    public TradeStatistics Stats { get; } = new();

    public decimal DailyLossUsd
    {
        get
        {
            var today = DateTime.UtcNow.Date;
            return TradeHistory
                .Where(t => t.ClosedAtUtc.Date == today && t.PnL < 0)
                .Sum(t => -t.PnL);
        }
    }

    public TimeSpan Uptime => Status == BotStatus.Running ? (DateTime.UtcNow - StartedAt) : TimeSpan.Zero;

    private readonly Dictionary<string, DateTime> _cooldownUntilUtc = new(StringComparer.OrdinalIgnoreCase);

    // ? Constructor that accepts BotSettingsEntity directly
    public BotState(BotSettingsEntity settingsEntity, Microsoft.Extensions.Logging.ILogger<BotState> logger)
    {
        // Convert entity to BotSettings
        Settings = new BotSettings
        {
            TradeMode = Enum.TryParse<TradeMode>(settingsEntity.TradeMode, out var tm) ? tm : TradeMode.Spot,
            StrategyMode = (StrategyMode)settingsEntity.StrategyModeValue,
            AutoScalperEnabled = settingsEntity.AutoScalperEnabled,
            ExecuteTrades = settingsEntity.ExecuteTrades,
            KillSwitch = settingsEntity.KillSwitch,
            TradingEnabled = settingsEntity.TradingEnabled,
            TargetUsdPerTrade = settingsEntity.TargetUsdPerTrade,
            MinUsdPerTrade = settingsEntity.MinUsdPerTrade,
            MaxOpenTrades = settingsEntity.MaxOpenTrades,
            MaxConcurrentPositions = settingsEntity.MaxConcurrentPositions,
            TakeProfitGrossPct = settingsEntity.TakeProfitGrossPct,
            StopLossGrossPct = settingsEntity.StopLossGrossPct,
            // ... other properties as needed
        };
        
        // ? CRITICAL: Initialize status based on settings
        var shouldRun = settingsEntity.TradingEnabled 
            && !settingsEntity.KillSwitch 
            && settingsEntity.AutoScalperEnabled;

        if (shouldRun)
        {
            MarkRunning();
        }
        else
        {
            Status = BotStatus.Stopped;
        }
    }

    // ? Parameterless constructor for backward compatibility
    public BotState()
    {
        Status = BotStatus.Stopped;
    }

    public bool IsSymbolInCooldown(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        lock (_cooldownUntilUtc)
        {
            return _cooldownUntilUtc.TryGetValue(symbol, out var until) && until > DateTime.UtcNow;
        }
    }

    public void PutSymbolCooldown(string symbol, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        lock (_cooldownUntilUtc)
        {
            _cooldownUntilUtc[symbol] = DateTime.UtcNow.Add(duration);
        }
    }

    public void RegisterTrade(TradeHistoryItem item)
    {
        TradeHistory.Add(item);
        Stats.Register(item.PnL);
    }

    public IReadOnlyList<BotLogEntry> GetLogs(int takeLast = 200)
    {
        lock (_logLock)
            return _logs.TakeLast(Math.Max(1, takeLast)).ToList();
    }

    public void AddLog(string level, string message, int max = 300)
    {
        lock (_logLock)
        {
            _logs.AddLast(new BotLogEntry
            {
                Level = level,
                Message = message,
                At = DateTimeOffset.UtcNow
            });

            while (_logs.Count > max)
                _logs.RemoveFirst();
        }
    }

    public void MarkRunning()
    {
        Status = BotStatus.Running;
        StartedAt = DateTime.UtcNow;
        TickCount = 0;
        LastError = null;
        AddLog("INFO", "Bot marked as Running");
    }

    public void MarkTick()
    {
        LastTickAt = DateTimeOffset.UtcNow;
        TickCount++;
    }

    public void MarkStopped()
    {
        Status = BotStatus.Stopped;
        AddLog("WARN", "Bot marked as Stopped");
    }

    public void MarkError(string error)
    {
        Status = BotStatus.Error;
        LastError = error;
        AddLog("ERROR", error);
    }

    public void ApplySettings(BotSettings settings)
    {
        Settings = settings;
        AddLog("INFO", $"Settings applied: Mode={settings.TradeMode}, Strategy={settings.StrategyMode}");
    }

    public void ApplyMarketSnapshot(BotMarketSnapshot snapshot)
        => Market = snapshot;

SELECT 
    JSON_VALUE(Message, '$.Code') AS BlockReason,
    COUNT(*) AS OccurrenceCount,
    MAX(AtUtc) AS LastSeen
FROM Events
WHERE 
    AtUtc >= DATEADD(HOUR, -1, GETUTCDATE())
    AND Type = 'GUARD_BLOCK'
GROUP BY JSON_VALUE(Message, '$.Code')
ORDER BY OccurrenceCount DESC;

-- ? Expected: BOT_NOT_RUNNING = 0 (or absent from results)
-- ? Acceptable: QUEUE_EMPTY, COOLDOWN, CAPITAL_TOO_SMALL