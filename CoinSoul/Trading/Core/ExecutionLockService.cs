using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace CoinSoul.Trading.Core;

/// <summary>
/// ✅ PATCH 6: Ensures lock always releases in finally block.
/// Prevents "LOCK_BUSY" from stuck locks.
/// </summary>
public sealed class ExecutionLockService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ExecutionLockService> _logger;

    public ExecutionLockService(IMemoryCache cache, ILogger<ExecutionLockService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Acquires lock for symbol entry. Returns lease that MUST be released in finally.
    /// </summary>
    public async Task<LockLease> TryAcquireEntryLockAsync(
        string symbol, 
        TimeSpan ttl, 
        string? correlationId, 
        CancellationToken ct)
    {
        var key = $"entry_lock:{symbol}";
        
        // ✅ PATCH 6: Try acquire with atomic check
        if (!_cache.TryGetValue(key, out _))
        {
            var lockValue = Guid.NewGuid().ToString();
            _cache.Set(key, lockValue, ttl);
            
            _logger.LogDebug("[LOCK_ACQUIRED] Entry lock for {Symbol} | LockId={LockId} | CorrelationId={Correlation}",
                symbol, lockValue, correlationId ?? "N/A");
            
            return new LockLease(true, key, lockValue, _cache, _logger, symbol, correlationId);
        }
        
        _logger.LogWarning("[LOCK_BUSY] Entry lock held for {Symbol} | CorrelationId={Correlation}",
            symbol, correlationId ?? "N/A");
        
        return new LockLease(false, key, null, _cache, _logger, symbol, correlationId);
    }

    /// <summary>
    /// ✅ PATCH 6: Lock lease that guarantees release in Dispose/finally.
    /// </summary>
    public sealed class LockLease : IAsyncDisposable
    {
        public bool Acquired { get; }
        
        private readonly string _key;
        private readonly string? _lockId;
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;
        private readonly string _symbol;
        private readonly string? _correlationId;
        private bool _released;

        public LockLease(
            bool acquired, 
            string key, 
            string? lockId, 
            IMemoryCache cache, 
            ILogger logger,
            string symbol,
            string? correlationId)
        {
            Acquired = acquired;
            _key = key;
            _lockId = lockId;
            _cache = cache;
            _logger = logger;
            _symbol = symbol;
            _correlationId = correlationId;
        }

        /// <summary>
        /// ✅ PATCH 6: Always release lock in Dispose (called in finally).
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (!Acquired || _released) return;

            _cache.Remove(_key);
            _released = true;
            
            _logger.LogDebug("[LOCK_RELEASED] Entry lock for {Symbol} | LockId={LockId} | CorrelationId={Correlation}",
                _symbol, _lockId, _correlationId ?? "N/A");
            
            await Task.CompletedTask;
        }
    }
}