using System.Collections.Concurrent;

namespace CoinSoul.Trading.Engine;

/// <summary>
/// Singleton store for symbol queue and cooldowns.
/// Shared across all DI scopes to solve queue isolation issue.
/// Thread-safe for concurrent access from MarketScannerService and TradingWorker.
/// </summary>
public sealed class SymbolQueueStore
{
    private readonly object _sync = new();

    // Keep priority ordering (highest score first)
    private readonly List<SymbolQueueManager.QueuedSymbol> _queue = new();
    private readonly Dictionary<string, DateTimeOffset> _cooldowns = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _symbolSet = new(StringComparer.OrdinalIgnoreCase);

    public int Count
    {
        get { lock (_sync) return _queue.Count; }
    }

    public IReadOnlyList<SymbolQueueManager.QueuedSymbol> Snapshot()
    {
        lock (_sync) return _queue.ToList();
    }

    /// <summary>
    /// Enqueue batch with deduplication and score-based sorting.
    /// Replaces existing symbols if new score is higher.
    /// </summary>
    public void EnqueueBatch(IEnumerable<SymbolQueueManager.QueuedSymbol> symbols, int maxQueueSize)
    {
        lock (_sync)
        {
            foreach (var s in symbols)
            {
                // Remove duplicates (keep best score)
                var idx = _queue.FindIndex(x => x.Symbol.Equals(s.Symbol, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0)
                {
                    if (s.Score > _queue[idx].Score)
                    {
                        _queue[idx] = s;
                    }
                }
                else
                {
                    _queue.Add(s);
                    _symbolSet.Add(s.Symbol);
                }
            }

            // Sort by tier first (A,B,C, then score desc), then trim
            _queue.Sort((a, b) => CompareQueuedSymbols(a, b));
            
            if (_queue.Count > maxQueueSize)
            {
                // Remove trimmed symbols from set
                for (int i = maxQueueSize; i < _queue.Count; i++)
                {
                    _symbolSet.Remove(_queue[i].Symbol);
                }
                _queue.RemoveRange(maxQueueSize, _queue.Count - maxQueueSize);
            }
        }
    }

    /// <summary>
    /// Dequeue highest-priority symbol (highest score).
    /// </summary>
    public bool TryDequeue(out SymbolQueueManager.QueuedSymbol? symbol)
    {
        lock (_sync)
        {
            if (_queue.Count == 0)
            {
                symbol = null;
                return false;
            }

            symbol = _queue[0];
            _queue.RemoveAt(0);
            _symbolSet.Remove(symbol.Symbol);
            return true;
        }
    }

    /// <summary>
    /// Check if symbol is in active cooldown.
    /// Auto-removes expired cooldowns.
    /// </summary>
    public bool IsCooldownActive(string symbol, DateTimeOffset now)
    {
        lock (_sync)
        {
            if (_cooldowns.TryGetValue(symbol, out var until))
            {
                if (until > now) return true;
                _cooldowns.Remove(symbol); // expired
            }
            return false;
        }
    }

    /// <summary>
    /// Mark symbol as in cooldown until specified time.
    /// </summary>
    public void MarkCooldown(string symbol, DateTimeOffset until)
    {
        lock (_sync) _cooldowns[symbol] = until;
    }


    public void ReplaceQueue(IEnumerable<SymbolQueueManager.QueuedSymbol> symbols, int maxQueueSize)
    {
        lock (_sync)
        {
            _queue.Clear();
            _symbolSet.Clear();
            foreach (var s in symbols)
            {
                if (_symbolSet.Add(s.Symbol))
                    _queue.Add(s);
            }
            _queue.Sort((a, b) => CompareQueuedSymbols(a, b));
            if (_queue.Count > maxQueueSize)
            {
                _queue.RemoveRange(maxQueueSize, _queue.Count - maxQueueSize);
                _symbolSet.Clear();
                foreach (var s in _queue) _symbolSet.Add(s.Symbol);
            }
        }
    }

    private static int CompareQueuedSymbols(SymbolQueueManager.QueuedSymbol a, SymbolQueueManager.QueuedSymbol b)
    {
        var ta = ExtractTier(a.Reason);
        var tb = ExtractTier(b.Reason);
        var tierCmp = ta.CompareTo(tb);
        if (tierCmp != 0) return tierCmp;
        return b.Score.CompareTo(a.Score);
    }

    private static int ExtractTier(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return 9;
        if (reason.StartsWith("A|", StringComparison.OrdinalIgnoreCase)) return 0;
        if (reason.StartsWith("B|", StringComparison.OrdinalIgnoreCase)) return 1;
        if (reason.StartsWith("C|", StringComparison.OrdinalIgnoreCase)) return 2;
        return 9;
    }

    /// <summary>
    /// Clear all queued symbols and cooldowns (for testing/reset).
    /// </summary>
    public void Clear()
    {
        lock (_sync)
        {
            _queue.Clear();
            _symbolSet.Clear();
            _cooldowns.Clear();
        }
    }
}