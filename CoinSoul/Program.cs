using Binance.Net;
using CoinSoul.Api.Controllers;
using CoinSoul.BinanceService.API;
using CoinSoul.BinanceService.AutoServices.AccountDataService;
using CoinSoul.BinanceService.AutoServices.SpotTradeService;
using CoinSoul.BinanceService.Base;
using CoinSoul.BinanceService.Services.SpotTradeService;
using CoinSoul.Components;
using CoinSoul.Components.Account;
using CoinSoul.Entities;
using CoinSoul.Infrastructure;
using CoinSoul.Infrastructure.Notifications;
using CoinSoul.Repository.DbContext;
using CoinSoul.Trading.Application;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine;
using CoinSoul.Trading.Engine.Adaptive;
using CoinSoul.Trading.Engine.Analytics;
using CoinSoul.Trading.Engine.Cache;
using CoinSoul.Trading.Engine.Observability;
using CoinSoul.Trading.Engine.Settings;
using CoinSoul.Trading.Engine.Streaming;
using CoinSoul.Trading.Engine.V2;
using CryptoExchange.Net.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MudBlazor;
using MudBlazor.Services;

var builder = WebApplication.CreateBuilder(args);

// Binance keys
APINames.ApiKey = builder.Configuration["Binance:ApiKey"] ?? "";
APINames.SecretKey = builder.Configuration["Binance:SecretKey"] ?? "";

// UI
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthorization();

// ✅ Add Controllers for API endpoints
builder.Services.AddControllers();

builder.Services.AddScoped<IdentityUserAccessor>();
builder.Services.AddScoped<IdentityRedirectManager>();

builder.Services.AddAutoMapper(AppDomain.CurrentDomain.GetAssemblies());

// ====================================================================
// BINANCE CLIENT
// ====================================================================
builder.Services.AddBinance(options =>
{
    options.ApiCredentials = new ApiCredentials(
        builder.Configuration["Binance:ApiKey"] ?? "",
        builder.Configuration["Binance:SecretKey"] ?? ""
    );

    options.AutoTimestamp = true;
    options.RequestTimeout = TimeSpan.FromSeconds(30);
    options.Environment = BinanceEnvironment.Live;
});

// ====================================================================
// INFRASTRUCTURE - Singleton Services
// ====================================================================
builder.Services.AddSingleton<BinanceApplicationService>();
builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<IExecutionModeDecider, ExecutionModeDecider>();

// ✅ CRITICAL: Memory Cache (required by TradingSafetyGate)
builder.Services.AddMemoryCache();

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
// ✅ CRITICAL FIX #1: SHARED QUEUE - SINGLETON (MUST BE BEFORE SCOPED SERVICES)
// ====================================================================
builder.Services.AddSingleton<SymbolQueueStore>();
builder.Services.AddSingleton<SymbolQueueManager>(); // ✅ CHANGED FROM AddScoped TO AddSingleton

// ====================================================================
// SCOPED SERVICES - Database & Trading Logic
// ====================================================================

// Core Trading Services
builder.Services.AddScoped<IMarketKlineProvider, MarketKlineProvider>();
builder.Services.AddScoped<ITradeExecutor, BinanceTradeExecutor>();
builder.Services.AddScoped<ISymbolValidator, BinanceSymbolValidator>();
builder.Services.AddScoped<IBestSymbolsService, BestSymbolsService>();
builder.Services.AddScoped<INotificationService, TradingNotificationService>();
builder.Services.AddScoped<IPortfolioService, BinancePortfolioService>();
builder.Services.AddScoped<IAccountTradeWriter, AccountTradeWriter>();

// ✅ CRITICAL: Missing Core Services
builder.Services.AddScoped<HybridEntryService>();
builder.Services.AddScoped<PortfolioRefreshService>();
builder.Services.AddScoped<QuantizationService>();
builder.Services.AddScoped<NetProfitExitService>();

// ✅ CRITICAL: BotSettingsService for UI components
builder.Services.AddScoped<CoinSoul.Trading.Engine.Settings.BotSettingsService>();

// Strategies
builder.Services.AddScoped<ManualStrategyA>();
builder.Services.AddScoped<ITradingStrategy, ManualStrategyA>();
builder.Services.AddScoped<ITradingStrategy, ScalperStrategyD>();

// Trading Engine
builder.Services.AddScoped<TradingEngine>();
builder.Services.AddScoped<ITradingEngine>(sp => sp.GetRequiredService<TradingEngine>());

// Engine Components - All Scoped (depend on DbContext)
builder.Services.AddScoped<AutoScalperPositionManager>();
// ✅ SymbolQueueManager is now SINGLETON (registered above) - DO NOT ADD HERE
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
builder.Services.AddScoped<AutoScalperStrategy>();
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
builder.Services.AddScoped<TradeHistorySyncService>();
builder.Services.AddScoped<PerformanceMetricsService>();

// ====================================================================
// BINANCE API SERVICES
// ====================================================================
builder.Services.AddHttpClient<ISpotTradeService, SpotTradeService>();
builder.Services.AddScoped<IAutoSpotTradeService, AutoSpotTradeService>();
builder.Services.AddScoped<IAutoAccountDataService, AutoAccountDataService>();

// ====================================================================
// BACKGROUND SERVICES - IHostedService
// ====================================================================
builder.Services.AddHostedService<TradingWorker>();
builder.Services.AddHostedService<EquityTrackingService>();
builder.Services.AddHostedService<BinanceTradeSyncService>();
builder.Services.AddHostedService<MarketScannerService>();
builder.Services.AddHostedService<StreamSubscriptionManager>();
builder.Services.AddHostedService<PositionReconciliationService>();
builder.Services.AddHostedService<BotSettingsValidationService>();

// ✅ OBSERVABILITY FOUNDATION
builder.Services.AddScoped<IEventWriter, DbEventWriter>();
builder.Services.AddScoped<TickEventGuard>();

// ====================================================================
// AUTHENTICATION
// ====================================================================
builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.ApplicationScheme;
    options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
})
.AddIdentityCookies();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/login";
    options.LogoutPath = "/login";
    options.AccessDeniedPath = "/login";
});

// ====================================================================
// DATABASE
// ====================================================================
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContextFactory<CoinSoulDbContext>(options =>
    options.UseSqlServer(
        connectionString,
        sqlOptions =>
        {
            sqlOptions.EnableRetryOnFailure(
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null
            );
            sqlOptions.MigrationsAssembly("CoinSoul.Repository");
        }));

builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ====================================================================
// IDENTITY
// ====================================================================
builder.Services.AddIdentityCore<AppUser>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddRoles<AppRole>()
    .AddEntityFrameworkStores<CoinSoulDbContext>()
    .AddSignInManager()
    .AddDefaultTokenProviders();

builder.Services.AddSingleton<IEmailSender<AppUser>, IdentityNoOpEmailSender>();

// ====================================================================
// MUDBLAZOR
// ====================================================================
builder.Services.AddMudServices(config =>
{
    config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.TopRight;
    config.SnackbarConfiguration.PreventDuplicates = false;
    config.SnackbarConfiguration.NewestOnTop = false;
    config.SnackbarConfiguration.ShowCloseIcon = true;
    config.SnackbarConfiguration.VisibleStateDuration = 3000;
    config.SnackbarConfiguration.HideTransitionDuration = 250;
    config.SnackbarConfiguration.ShowTransitionDuration = 250;
    config.SnackbarConfiguration.SnackbarVariant = Variant.Filled;
});

// ====================================================================
// BUILD & CONFIGURE APP
// ====================================================================
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAntiforgery();

app.UseAuthentication();
app.UseAuthorization();

await app.SeedAdminAsync();

app.MapControllers();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();

// ====================================================================
// TOP-LEVEL TYPE DECLARATIONS
// ====================================================================

public class TradeHistorySyncService
{
    private readonly IAccountTradeWriter _tradeWriter;
    private readonly IAutoAccountDataService _accountService;

    public TradeHistorySyncService(
        IAccountTradeWriter tradeWriter,
        IAutoAccountDataService accountService)
    {
        _tradeWriter = tradeWriter;
        _accountService = accountService;
    }

    public async Task SyncTradesAsync(string symbol, DateTime fromUtc, DateTime toUtc, CancellationToken ct)
    {
        var binanceTrades = await _accountService.GetUserTradesAsync(
            symbol, null, fromUtc, toUtc, 1000, null, null, ct);

        var entities = binanceTrades.Select(t => new AccountTradeEntity
        {
            TradeId = t.Id,
            Symbol = symbol,
            Side = t.IsBuyer ? "BUY" : "SELL",
            Price = t.Price,
            Quantity = t.Quantity,
            QuoteQty = t.QuoteQuantity,
            Commission = t.Fee,
            CommissionAsset = t.FeeAsset,
            IsMaker = t.IsMaker,
            TradeTimeUtc = t.Timestamp.Kind == DateTimeKind.Utc 
                ? t.Timestamp 
                : t.Timestamp.ToUniversalTime(),
            Source = "SYNC",
            OrderId = t.OrderId
        }).ToList();

        var savedCount = await _tradeWriter.SaveBatchAsync(entities, ct);
        Console.WriteLine($"Synced {savedCount} trades for {symbol}");
    }
}


