using CoinSoul.Entities;
using CoinSoul.Repository.DbContext;
using Microsoft.EntityFrameworkCore;

namespace CoinSoul.Trading.Engine;

public sealed class ExecutionGuardService
{
    private readonly CoinSoulDbContext _db;
    private const int LockTimeoutSeconds = 30;
    private const int IdempotencyWindowMinutes = 5;

    public ExecutionGuardService(CoinSoulDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Acquire exclusive lock for symbol entry/exit operations
    /// </summary>
    public async Task<bool> TryAcquireSymbolLockAsync(string symbol, string lockType, CancellationToken ct)
    {
        try
        {
            // Clean expired locks first
            await CleanExpiredLocksAsync(ct);

            // Check if lock already exists
            var existingLock = await _db.ExecutionLocks
                .FirstOrDefaultAsync(l => l.Symbol == symbol && l.LockType == lockType && l.ExpiresAtUtc > DateTime.UtcNow, ct);

            if (existingLock != null)
            {
                return false; // Lock already held
            }

            // Create new lock
            var newLock = new ExecutionLockEntity
            {
                Symbol = symbol,
                LockType = lockType,
                AcquiredAtUtc = DateTime.UtcNow,
                ExpiresAtUtc = DateTime.UtcNow.AddSeconds(LockTimeoutSeconds)
            };

            _db.ExecutionLocks.Add(newLock);
            await _db.SaveChangesAsync(ct);

            return true;
        }
        catch (DbUpdateException)
        {
            // Concurrent insert attempt - lock already acquired
            return false;
        }
    }

    /// <summary>
    /// Release symbol lock
    /// </summary>
    public async Task ReleaseSymbolLockAsync(string symbol, string lockType, CancellationToken ct)
    {
        try
        {
            var locks = await _db.ExecutionLocks
                .Where(l => l.Symbol == symbol && l.LockType == lockType)
                .ToListAsync(ct);

            _db.ExecutionLocks.RemoveRange(locks);
            await _db.SaveChangesAsync(ct);
        }
        catch
        {
            // Ignore release errors
        }
    }

    /// <summary>
    /// Acquire global entry slot (enforce max concurrent positions)
    /// </summary>
    public async Task<bool> TryAcquireGlobalEntrySlotAsync(int maxConcurrentPositions, CancellationToken ct)
    {
        if (maxConcurrentPositions <= 0)
            return true; // Unlimited

        await CleanExpiredLocksAsync(ct);

        var activeGlobalLocks = await _db.ExecutionLocks
            .CountAsync(l => l.LockType == "GLOBAL" && l.ExpiresAtUtc > DateTime.UtcNow, ct);

        if (activeGlobalLocks >= maxConcurrentPositions)
            return false;

        var globalLock = new ExecutionLockEntity
        {
            Symbol = "GLOBAL",
            LockType = "GLOBAL",
            AcquiredAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddSeconds(LockTimeoutSeconds)
        };

        _db.ExecutionLocks.Add(globalLock);
        await _db.SaveChangesAsync(ct);

        return true;
    }

    /// <summary>
    /// Release global entry slot
    /// </summary>
    public async Task ReleaseGlobalEntrySlotAsync(CancellationToken ct)
    {
        try
        {
            var globalLock = await _db.ExecutionLocks
                .Where(l => l.LockType == "GLOBAL")
                .OrderBy(l => l.AcquiredAtUtc)
                .FirstOrDefaultAsync(ct);

            if (globalLock != null)
            {
                _db.ExecutionLocks.Remove(globalLock);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch
        {
            // Ignore
        }
    }

    /// <summary>
    /// Check idempotency - prevents duplicate entries
    /// </summary>
    public async Task<(bool AlreadyExecuted, long? OrderId)> CheckIdempotencyAsync(string idempotencyKey, CancellationToken ct)
    {
        // Clean old attempts (older than window)
        var cutoff = DateTime.UtcNow.AddMinutes(-IdempotencyWindowMinutes);
        var oldAttempts = await _db.ExecutionAttempts
            .Where(a => a.AttemptedAtUtc < cutoff)
            .ToListAsync(ct);

        if (oldAttempts.Any())
        {
            _db.ExecutionAttempts.RemoveRange(oldAttempts);
            await _db.SaveChangesAsync(ct);
        }

        // Check if this key was already executed successfully
        var existingAttempt = await _db.ExecutionAttempts
            .FirstOrDefaultAsync(a => a.IdempotencyKey == idempotencyKey && a.Success, ct);

        if (existingAttempt != null)
        {
            return (true, existingAttempt.OrderId);
        }

        return (false, null);
    }

    /// <summary>
    /// Record execution attempt
    /// </summary>
    public async Task RecordAttemptAsync(
        string idempotencyKey,
        string symbol,
        string attemptType,
        bool success,
        long? orderId,
        string? result,
        CancellationToken ct)
    {
        var attempt = new ExecutionAttemptEntity
        {
            IdempotencyKey = idempotencyKey,
            Symbol = symbol,
            AttemptType = attemptType,
            Success = success,
            AttemptedAtUtc = DateTime.UtcNow,
            OrderId = orderId,
            Result = result
        };

        _db.ExecutionAttempts.Add(attempt);
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Clean expired locks periodically
    /// </summary>
    private async Task CleanExpiredLocksAsync(CancellationToken ct)
    {
        try
        {
            var expired = await _db.ExecutionLocks
                .Where(l => l.ExpiresAtUtc <= DateTime.UtcNow)
                .ToListAsync(ct);

            if (expired.Any())
            {
                _db.ExecutionLocks.RemoveRange(expired);
                await _db.SaveChangesAsync(ct);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    /// <summary>
    /// Generate deterministic idempotency key for entry
    /// </summary>
    public static string GenerateEntryKey(string symbol, decimal tradeSize)
    {
        var minuteBucket = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        return $"ENTRY:{symbol}:{minuteBucket}:{tradeSize:0.00}:v1";
    }

    /// <summary>
    /// Generate deterministic idempotency key for exit
    /// </summary>
    public static string GenerateExitKey(string symbol, int positionId)
    {
        var minuteBucket = DateTime.UtcNow.ToString("yyyyMMddHHmm");
        return $"EXIT:{symbol}:{positionId}:{minuteBucket}:v1";
    }
}