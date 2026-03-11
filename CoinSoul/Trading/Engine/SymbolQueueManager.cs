using CoinSoul.Trading.Application;
using CoinSoul.Trading.Core;
using CoinSoul.Trading.Engine.Observability;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// ✅ PRODUCTION HOTFIX: Now a SINGLETON with strict symbol validation.
/// Shared queue across all scopes (MarketScannerService + Orchestrator ticks).
/// Thread-safe via SymbolQueueStore locking.
/// </summary>
public sealed class SymbolQueueManager
{
    public sealed record QueuedSymbol(string Symbol, decimal Score, string Reason);

    private readonly SymbolQueueStore _store;
    private readonly ILogger<SymbolQueueManager> _logger;
    private readonly IClock _clock;
    private readonly bool _enableDiagnosticLogging;

    // ✅ PATCH 2: Strict symbol validation regex
    private static readonly Regex ValidSymbolRegex = new(@"^[A-Z0-9]{2,20}USDT$", RegexOptions.Compiled);

    public int MaxQueueSize { get; set; } = 160;
    public TimeSpan FairnessPenalty { get; set; } = TimeSpan.FromSeconds(10);

    public SymbolQueueManager(
        SymbolQueueStore store,
        ILogger<SymbolQueueManager> logger,
        IClock clock,
        IConfiguration configuration)
    {
        _store = store;
        _logger = logger;
        _clock = clock;
        _enableDiagnosticLogging = configuration.GetValue<bool>("EnableDiagnosticLogging", false);
        MaxQueueSize = Math.Clamp(configuration.GetValue<int>("Trading:QueueSize", configuration.GetValue<int>("QueueSize", 160)), 20, 250);
    }

    public IReadOnlyList<QueuedSymbol> Snapshot() => _store.Snapshot();

    public void ReplaceQueue(IEnumerable<QueuedSymbol> symbols, string? correlationId = null)
    {
        var sanitized = symbols
            .Select(s => s with { Symbol = SanitizeSymbol(s.Symbol) })
            .Where(s => IsValidSymbol(s.Symbol) && !IsInCooldown(s.Symbol))
            .ToList();
        _store.ReplaceQueue(sanitized, MaxQueueSize);
        _logger.LogCritical("[QUEUE_REFRESH] Replaced queue with {Count} symbols | QueueSize={QueueSize}/{Max} | CorrelationId={Correlation}",
            sanitized.Count, _store.Count, MaxQueueSize, correlationId ?? "N/A");
    }

    /// <summary>
    /// ✅ PATCH 2: Validates symbol format strictly (^[A-Z0-9]{2,20}USDT$).
    /// Rejects non-ASCII, spaces, punctuation, question marks.
    /// </summary>
    private static bool IsValidSymbol(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return false;
        var trimmed = symbol.Trim().ToUpperInvariant();
        return ValidSymbolRegex.IsMatch(trimmed);
    }

    /// <summary>
    /// ✅ PATCH 2: Sanitize symbol - trim and uppercase.
    /// </summary>
    private static string SanitizeSymbol(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        return raw.Trim().ToUpperInvariant();
    }

    public void MarkCooldown(string symbol, TimeSpan duration)
    {
        var sanitized = SanitizeSymbol(symbol);
        
        if (!IsValidSymbol(sanitized))
        {
            _logger.LogWarning("[QUEUE_COOLDOWN_REJECTED] Invalid symbol | Raw=\"{Raw}\"", symbol);
            return;
        }

        if (duration <= TimeSpan.Zero) return;

        _store.MarkCooldown(sanitized, _clock.UtcNowOffset.Add(duration));
        
        if (_enableDiagnosticLogging)
        {
            _logger.LogDebug("[COOLDOWN_SET] {Symbol} for {Duration}s", 
                sanitized, duration.TotalSeconds);
        }
    }

    public bool IsInCooldown(string symbol)
    {
        return _store.IsCooldownActive(symbol, _clock.UtcNowOffset);
    }

    public void Enqueue(QueuedSymbol symbol)
    {
        var sanitized = SanitizeSymbol(symbol.Symbol);
        
        if (!IsValidSymbol(sanitized))
        {
            _logger.LogWarning("[QUEUE_ENQUEUE_REJECTED] Invalid symbol | Raw=\"{Raw}\"", symbol.Symbol);
            return;
        }

        var validSymbol = symbol with { Symbol = sanitized };

        if (IsInCooldown(sanitized))
        {
            _logger.LogDebug("[QUEUE_ENQUEUE_COOLDOWN] Symbol {Symbol} skipped - in cooldown", sanitized);
            return;
        }
        
        if (_store.Count >= MaxQueueSize)
        {
            _logger.LogDebug("[QUEUE_ENQUEUE_FULL] Symbol {Symbol} skipped - queue full ({Count}/{Max})",
                sanitized, _store.Count, MaxQueueSize);
            return;
        }

        _store.EnqueueBatch(new[] { validSymbol }, MaxQueueSize);
        
        _logger.LogInformation("[QUEUE_ADD] {Symbol} added | Score={Score:F1}, QueueSize={Count}/{Max}",
            sanitized, validSymbol.Score, _store.Count, MaxQueueSize);
    }

    /// <summary>
    /// ✅ PATCH 2: Enhanced batch enqueue with rejection tracking and event logging.
    /// </summary>
    public void EnqueueBatch(IEnumerable<QueuedSymbol> symbols, string? correlationId = null)
    {
        var symbolList = symbols.ToList();
        
        _logger.LogCritical("[QUEUE_ENQUEUE_BATCH_START] Attempting to enqueue {Count} symbols | QueueSize={QueueSize}/{Max} | CorrelationId={Correlation}",
            symbolList.Count, _store.Count, MaxQueueSize, correlationId ?? "N/A");
        
        if (symbolList.Count == 0)
        {
            _logger.LogWarning("[QUEUE_ENQUEUE_EMPTY] Attempted to enqueue 0 symbols");
            return;
        }

        var added = 0;
        var skippedCooldown = 0;
        var skippedFull = 0;
        var rejectedInvalid = 0; // ✅ PATCH 2: Track rejected symbols
        
        var validSymbols = new List<QueuedSymbol>();
        
        foreach (var symbol in symbolList)
        {
            var sanitized = SanitizeSymbol(symbol.Symbol);
            
            // ✅ PATCH 2: Strict validation
            if (!IsValidSymbol(sanitized))
            {
                rejectedInvalid++;
                _logger.LogWarning("[QUEUE_BATCH_INVALID] Rejected invalid symbol | Raw=\"{Raw}\" Sanitized=\"{San}\"",
                    symbol.Symbol, sanitized);
                
                // ✅ PATCH 2: Emit tick event for rejected symbol (defensive)
                // Note: IEventWriter requires scoped DbContext, so we log only here
                // In a future enhancement, pass IServiceProvider to emit events
                continue;
            }
            
            if (IsInCooldown(sanitized))
            {
                skippedCooldown++;
                continue;
            }
            
            if (_store.Count >= MaxQueueSize && validSymbols.Count == 0)
            {
                skippedFull++;
                continue;
            }
            
            validSymbols.Add(symbol with { Symbol = sanitized });
        }

        if (validSymbols.Count > 0)
        {
            var countBefore = _store.Count;
            _store.EnqueueBatch(validSymbols, MaxQueueSize);
            var countAfter = _store.Count;
            added = countAfter - countBefore;
        }

        // ✅ PATCH 2: Enhanced log includes Rejected count
        _logger.LogCritical(
            "[QUEUE_ENQUEUE_BATCH_RESULT] Added={Added}, Rejected={Rejected}, SkippedCooldown={Cooldown}, " +
            "SkippedFull={Full} | Total={Total}, QueueSize={QueueSize}/{Max} | CorrelationId={Correlation}",
            added, rejectedInvalid, skippedCooldown, skippedFull,
            symbolList.Count, _store.Count, MaxQueueSize, correlationId ?? "N/A");
    }

    /// <summary>
    /// ✅ PATCH 2: Dequeue with defensive validation - drop invalid symbols.
    /// </summary>
    public bool TryDequeue(out QueuedSymbol? q, string? correlationId = null, int maxRetries = 5)
    {
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            if (!_store.TryDequeue(out q))
            {
                if (attempt == 0)
                {
                    _logger.LogInformation("[QUEUE_EMPTY] No symbols available | MaxQueueSize={MaxQueueSize} | CorrelationId={Correlation}",
                        MaxQueueSize, correlationId ?? "N/A");
                }
                
                return false;
            }

            // Skip if in cooldown (race condition)
            if (IsInCooldown(q.Symbol))
            {
                if (_enableDiagnosticLogging)
                {
                    _logger.LogDebug("[QUEUE_SKIP_COOLDOWN] {Symbol} (attempt {Attempt}/{Max})", 
                        q.Symbol, attempt + 1, maxRetries);
                }
                continue;
            }

            // ✅ PATCH 2: Defensive validation - drop invalid symbols
            if (!IsValidSymbol(q.Symbol))
            {
                _logger.LogWarning("[QUEUE_DEQUEUE_INVALID] Dropping invalid symbol: \"{Symbol}\" | CorrelationId={Correlation}", 
                    q.Symbol, correlationId ?? "N/A");
                continue; // Try next symbol
            }

            // Success!
            _logger.LogInformation("[QUEUE_PULL] {Symbol}, Score={Score:F1} | QueueSize={QueueSize}/{Max} | CorrelationId={Correlation}",
                q.Symbol, q.Score, _store.Count, MaxQueueSize, correlationId ?? "N/A");

            return true;
        }

        // All retries exhausted
        _logger.LogWarning("[QUEUE_DEQUEUE_RETRIES_EXHAUSTED] Failed after {Max} attempts | CorrelationId={Correlation}", 
            maxRetries, correlationId ?? "N/A");
        q = null;
        return false;
    }

    public Task<QueuedSymbol?> DequeueAsync(BotSettings settings, Action<string> log, CancellationToken ct)
    {
        if (_enableDiagnosticLogging)
        {
            _logger.LogWarning("[DIAG_DEQUEUE_START] QueueCount={Count}", _store.Count);
        }

        if (TryDequeue(out var q, null))
        {
            log($"[QUEUE_PULL] {q.Symbol} score={q.Score:F1}");
            return Task.FromResult<QueuedSymbol?>(q);
        }

        if (_enableDiagnosticLogging)
        {
            _logger.LogWarning("[DIAG_DEQUEUE_EMPTY] Queue is empty");
        }

        return Task.FromResult<QueuedSymbol?>(null);
    }

    public void Requeue(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol)) return;
        
        var sanitized = SanitizeSymbol(symbol);
        if (!IsValidSymbol(sanitized)) return;
        if (IsInCooldown(sanitized)) return;

        _store.EnqueueBatch(new[] { new QueuedSymbol(sanitized, 0m, "Requeue") }, MaxQueueSize);
    }
}
