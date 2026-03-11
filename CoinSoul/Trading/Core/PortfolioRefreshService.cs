using Binance.Net.Interfaces.Clients;
using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Core;

/// <summary>
/// Manages portfolio balance refresh with throttling and capital guards
/// </summary>
public sealed class PortfolioRefreshService
{
    private readonly IBinanceRestClient _binanceClient;
    private readonly IDbContextFactory<CoinSoulDbContext> _dbFactory;
    private readonly ILogger<PortfolioRefreshService> _logger;

    // Refresh throttle: prevent excessive API calls
    private DateTime _lastRefreshUtc = DateTime.MinValue;
    private PortfolioSnapshot? _cachedSnapshot;

    public PortfolioRefreshService(
        IBinanceRestClient binanceClient,
        IDbContextFactory<CoinSoulDbContext> dbFactory,
        ILogger<PortfolioRefreshService> logger)
    {
        _binanceClient = binanceClient;
        _dbFactory = dbFactory;
        _logger = logger;
    }

    /// <summary>
    /// Refreshes portfolio balances from Binance with throttling
    /// </summary>
    public async Task<PortfolioSnapshot> RefreshAsync(
        BotSettingsEntity settings,
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var timeSinceLastRefresh = now - _lastRefreshUtc;

        // ✅ THROTTLE: Don't refresh more than once per BalanceRefreshCooldownMs
        if (!forceRefresh && 
            _cachedSnapshot != null && 
            timeSinceLastRefresh.TotalMilliseconds < settings.BalanceRefreshCooldownMs)
        {
            _logger.LogDebug("[BALANCE_CACHED] Using cached snapshot from {Ago:F1}s ago",
                timeSinceLastRefresh.TotalSeconds);
            return _cachedSnapshot;
        }

        try
        {
            // Fetch account info from Binance
            var accountResult = await _binanceClient.SpotApi.Account.GetAccountInfoAsync(ct: ct);

            if (!accountResult.Success || accountResult.Data == null)
            {
                _logger.LogWarning("[BALANCE_REFRESH_FAIL] {Error}", 
                    accountResult.Error?.Message ?? "Unknown error");

                // Return cached if available
                if (_cachedSnapshot != null)
                    return _cachedSnapshot;

                throw new InvalidOperationException($"Balance refresh failed: {accountResult.Error?.Message}");
            }

            var usdtBalance = accountResult.Data.Balances
                .FirstOrDefault(b => b.Asset == "USDT");

            if (usdtBalance == null)
            {
                _logger.LogError("[BALANCE_REFRESH_FAIL] USDT balance not found");
                throw new InvalidOperationException("USDT balance not found in account");
            }

            var freeUsdt = usdtBalance.Available;
            var lockedUsdt = usdtBalance.Locked;
            var totalUsdt = usdtBalance.Total;

            _logger.LogInformation("[BALANCE_REFRESH] Free=${Free:N2} Locked=${Locked:N2} Total=${Total:N2}",
                freeUsdt, lockedUsdt, totalUsdt);

            var snapshot = new PortfolioSnapshot
            {
                RefreshedAtUtc = now,
                FreeUsdt = freeUsdt,
                LockedUsdt = lockedUsdt,
                TotalUsdt = totalUsdt
            };

            _cachedSnapshot = snapshot;
            _lastRefreshUtc = now;

            // ✅ Log to TradingEvents
            await LogBalanceEventAsync(snapshot, ct);

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BALANCE_REFRESH_ERROR]");

            // Return cached if available
            if (_cachedSnapshot != null)
            {
                _logger.LogWarning("[BALANCE_REFRESH_ERROR] Using stale cache");
                return _cachedSnapshot;
            }

            throw;
        }
    }

    /// <summary>
    /// Checks if sufficient capital is available for new entry
    /// </summary>
    public async Task<CapitalCheckResult> CheckCapitalAvailabilityAsync(
        decimal requiredUsdt,
        BotSettingsEntity settings,
        CancellationToken ct = default)
    {
        var snapshot = await RefreshAsync(settings, forceRefresh: false, ct);

        var reservedUsdt = settings.MinFreeUsdtReserve;
        var availableForTrading = snapshot.FreeUsdt - reservedUsdt;

        _logger.LogDebug("[CAPITAL_CHECK] Required=${Required:N2} Available=${Available:N2} (Free=${Free:N2} - Reserve=${Reserve:N2})",
            requiredUsdt, availableForTrading, snapshot.FreeUsdt, reservedUsdt);

        // Check 1: Enough free capital
        if (availableForTrading < requiredUsdt)
        {
            var reason = $"Insufficient capital: Available=${availableForTrading:N2} Required=${requiredUsdt:N2}";
            _logger.LogWarning("[CAPITAL_BLOCK] {Reason}", reason);

            await LogCapitalBlockEventAsync(reason, requiredUsdt, snapshot.FreeUsdt, ct);

            return new CapitalCheckResult
            {
                Allowed = false,
                Reason = reason,
                AvailableUsdt = availableForTrading,
                Snapshot = snapshot
            };
        }

        // Check 2: Minimum position size
        if (requiredUsdt < settings.MinUsdtToOpenNewPosition)
        {
            var reason = $"Below minimum: Required=${requiredUsdt:N2} Min=${settings.MinUsdtToOpenNewPosition:N2}";
            _logger.LogWarning("[CAPITAL_BLOCK] {Reason}", reason);

            await LogCapitalBlockEventAsync(reason, requiredUsdt, snapshot.FreeUsdt, ct);

            return new CapitalCheckResult
            {
                Allowed = false,
                Reason = reason,
                AvailableUsdt = availableForTrading,
                Snapshot = snapshot
            };
        }

        return new CapitalCheckResult
        {
            Allowed = true,
            Reason = "Capital check passed",
            AvailableUsdt = availableForTrading,
            Snapshot = snapshot
        };
    }

    private async Task LogBalanceEventAsync(PortfolioSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = "INFO",
                Type = "BALANCE_REFRESH",
                Symbol = null,
                Message = $"Free=${snapshot.FreeUsdt:N2} Locked=${snapshot.LockedUsdt:N2} Total=${snapshot.TotalUsdt:N2}"
            });

            await db.SaveChangesAsync(ct);
        }
        catch { }
    }

    private async Task LogCapitalBlockEventAsync(
        string reason,
        decimal required,
        decimal free,
        CancellationToken ct)
    {
        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync(ct);

            db.TradingEvents.Add(new TradingEventEntity
            {
                AtUtc = DateTimeOffset.UtcNow,
                Level = "WARN",
                Type = "CAPITAL_BLOCK",
                Symbol = null,
                Message = $"{reason} (Required=${required:N2}, Free=${free:N2})"
            });

            await db.SaveChangesAsync(ct);
        }
        catch { }
    }
}

/// <summary>
/// Portfolio balance snapshot
/// </summary>
public sealed record PortfolioSnapshot
{
    public DateTime RefreshedAtUtc { get; init; }
    public decimal FreeUsdt { get; init; }
    public decimal LockedUsdt { get; init; }
    public decimal TotalUsdt { get; init; }
}

/// <summary>
/// Result of capital availability check
/// </summary>
public sealed record CapitalCheckResult
{
    public bool Allowed { get; init; }
    public string Reason { get; init; } = "";
    public decimal AvailableUsdt { get; init; }
    public PortfolioSnapshot Snapshot { get; init; } = null!;
}