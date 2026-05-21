namespace Contriwork.MarketData.Internal;

/// <summary>
/// TTL cache with LRU eviction. CONTRACT.md §6. Not thread-safe — callers
/// sharing one instance must serialize access (the orchestrator runs on a
/// single logical flow per request).
/// </summary>
/// <typeparam name="TValue">Cached value type.</typeparam>
internal sealed class TtlCache<TValue>
{
    private readonly int maxEntries;
    private readonly IClock clock;
    private readonly LinkedList<string> lru = new();
    private readonly Dictionary<string, Entry> store = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="TtlCache{TValue}"/> class.</summary>
    /// <param name="maxEntries">Maximum entry count before LRU eviction.</param>
    /// <param name="clock">Time source.</param>
    public TtlCache(int maxEntries, IClock clock)
    {
        if (maxEntries < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEntries), "must be >= 1");
        }

        this.maxEntries = maxEntries;
        this.clock = clock;
    }

    /// <summary>Current entry count.</summary>
    public int Count => this.store.Count;

    /// <summary>Try to read a cached value.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="value">The cached value when present and unexpired.</param>
    /// <returns><c>true</c> on a live hit.</returns>
    public bool TryGet(string key, out TValue value)
    {
        if (this.store.TryGetValue(key, out var entry))
        {
            if (this.clock.Monotonic() >= entry.ExpiresAt)
            {
                this.Remove(key);
            }
            else
            {
                this.lru.Remove(entry.Node);
                this.lru.AddLast(entry.Node);
                value = entry.Value;
                return true;
            }
        }

        value = default!;
        return false;
    }

    /// <summary>Store a value with a TTL. A non-positive TTL is a no-op.</summary>
    /// <param name="key">Cache key.</param>
    /// <param name="value">Value to store.</param>
    /// <param name="ttlSeconds">Time-to-live in seconds.</param>
    public void Set(string key, TValue value, double ttlSeconds)
    {
        if (ttlSeconds <= 0)
        {
            return;
        }

        var expiresAt = this.clock.Monotonic() + ttlSeconds;
        if (this.store.TryGetValue(key, out var existing))
        {
            this.lru.Remove(existing.Node);
            this.lru.AddLast(existing.Node);
            this.store[key] = new Entry(value, expiresAt, existing.Node);
            return;
        }

        while (this.store.Count >= this.maxEntries && this.lru.First is { } oldest)
        {
            this.lru.RemoveFirst();
            this.store.Remove(oldest.Value);
        }

        var node = this.lru.AddLast(key);
        this.store[key] = new Entry(value, expiresAt, node);
    }

    private void Remove(string key)
    {
        if (this.store.TryGetValue(key, out var entry))
        {
            this.lru.Remove(entry.Node);
            this.store.Remove(key);
        }
    }

    private readonly record struct Entry(TValue Value, double ExpiresAt, LinkedListNode<string> Node);
}
