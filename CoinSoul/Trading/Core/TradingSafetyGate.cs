using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Core;

public interface ITradingSafetyGate
{
    Task<SafetyDecision> CanPlaceOrderAsync(string symbol, string action, CancellationToken ct = default);
    Task<BotSettingsEntity> GetSettingsAsync(CancellationToken ct = default);
}

public sealed record SafetyDecision(
    bool Allowed,
    bool DryRun,
    string Reason,
    BotSettingsEntity Settings);

public sealed class TradingSafetyGate : ITradingSafetyGate
{
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TradingSafetyGate> _logger;
    private const string CacheKey = "BotSettings";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromSeconds(3);

    public TradingSafetyGate(
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        IMemoryCache cache,
        ILogger<TradingSafetyGate> logger)
    {
        _dbFactory = dbFactory;
        _cache = cache;
        _logger = logger;
    }

    public async Task<SafetyDecision> CanPlaceOrderAsync(
        string symbol,
        string action,
        CancellationToken ct = default)
    {
        var settings = await GetSettingsAsync(ct);

        // Check 1: KillSwitch (highest priority)
        if (settings.KillSwitch)
        {
            _logger.LogCritical("[KILL_SWITCH] All trading blocked! Symbol={Symbol} Action={Action}", 
                symbol, action);
            
            await LogSafetyEventAsync("KILL_SWITCH", symbol, 
                $"Trading blocked by KillSwitch: {action}", ct);
            
            return new SafetyDecision(
                Allowed: false,
                DryRun: false,
                Reason: "KillSwitch is active - all trading blocked",
                Settings: settings);
        }

        // Check 2: ExecuteTrades flag (Dry Run mode)
        if (!settings.ExecuteTrades)
        {
            _logger.LogWarning("[DRY_RUN] Simulating order: Symbol={Symbol} Action={Action}", 
                symbol, action);
            
            return new SafetyDecision(
                Allowed: true,
                DryRun: true,
                Reason: "Dry Run Mode - no real orders placed",
                Settings: settings);
        }

        // Check 3: Risk Guard - StopUntilUtc
        if (settings.StopUntilUtc.HasValue && settings.StopUntilUtc.Value > DateTime.UtcNow)
        {
            var reason = $"Risk Stop active until {settings.StopUntilUtc:yyyy-MM-dd HH:mm} UTC";
            _logger.LogError("[RISK_STOP] {Reason} Symbol={Symbol}", reason, symbol);
            
            await LogSafetyEventAsync("RISK_STOP", symbol, reason, ct);
            
            return new SafetyDecision(
                Allowed: false,
                DryRun: false,
                Reason: reason,
                Settings: settings);
        }

        // Check 4: Risk Guard - PauseUntilUtc
        if (settings.PauseUntilUtc.HasValue && settings.PauseUntilUtc.Value > DateTime.UtcNow)
        {
            var reason = $"Risk Pause active until {settings.PauseUntilUtc:yyyy-MM-dd HH:mm} UTC";
            _logger.LogWarning("[RISK_PAUSE] {Reason} Symbol={Symbol}", reason, symbol);
            
            await LogSafetyEventAsync("RISK_PAUSE", symbol, reason, ct);
            
            return new SafetyDecision(
                Allowed: false,
                DryRun: false,
                Reason: reason,
                Settings: settings);
        }

        // All checks passed
        return new SafetyDecision(
            Allowed: true,
            DryRun: false,
            Reason: "All safety checks passed",
            Settings: settings);
    }

    public async Task<BotSettingsEntity> GetSettingsAsync(CancellationToken ct = default)
    {
        // Try cache first
        if (_cache.TryGetValue(CacheKey, out BotSettingsEntity? cached) && cached != null)
            return cached;

        // Load from DB
        await using var db = await _dbFactory.CreateDbContextAsync(ct);
        
        var settings = await db.BotSettings
            .AsNoTracking()
            .FirstOrDefaultAsync(ct);

        // Seed if not exists
        if (settings == null)
        {
            _logger.LogWarning("[SETTINGS_SEED] Creating default BotSettings");
            
            settings = new BotSettingsEntity
            {
                ExecuteTrades = false, // Safe default: DRY RUN
                KillSwitch = false
            };

            db.BotSettings.Add(settings);
            await db.SaveChangesAsync(ct);
        }

        // Cache for 3 seconds
        _cache.Set(CacheKey, settings, CacheExpiration);
        
        return settings;
    }

    private async Task LogSafetyEventAsync(
        string type,
        string symbol,
        string message,
        CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);
            
            db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = "CRITICAL",
                Type = type,
                Symbol = symbol,
                Message = message
            });

            await db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SAFETY_LOG_ERROR] Failed to log safety event");
        }
    }
}