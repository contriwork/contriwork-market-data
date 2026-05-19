namespace Contriwork.MarketData;

/// <summary>Rate-limit fall-through strategy. CONTRACT.md §8.</summary>
public enum RateLimitStrategy
{
    /// <summary>Surface <c>RATE_LIMITED</c> once the retry budget is spent.</summary>
    Bubble,

    /// <summary>Advance to the next adapter in the chain on exhaustion.</summary>
    Fallthrough,
}

/// <summary>TTL cache configuration. CONTRACT.md §6, §8.</summary>
public sealed record CacheConfig
{
    /// <summary>Whether the cache is active. Disabled by default.</summary>
    public bool Enabled { get; init; }

    /// <summary>Spot-price TTL in seconds.</summary>
    public int SpotTtlSeconds { get; init; } = 5;

    /// <summary>OHLCV TTL in seconds.</summary>
    public int OhlcvTtlSeconds { get; init; } = 60;

    /// <summary>Order-book TTL in seconds.</summary>
    public int OrderBookTtlSeconds { get; init; } = 1;

    /// <summary>Maximum cache entries before LRU eviction.</summary>
    public int MaxEntries { get; init; } = 10_000;

    /// <summary>Validate the configuration invariants.</summary>
    /// <exception cref="ArgumentException">When a value is out of range.</exception>
    public void Validate()
    {
        if (this.SpotTtlSeconds < 0 || this.OhlcvTtlSeconds < 0 || this.OrderBookTtlSeconds < 0)
        {
            throw new ArgumentException("cache TTL values must be >= 0");
        }

        if (this.MaxEntries < 1)
        {
            throw new ArgumentException("cache MaxEntries must be >= 1");
        }
    }
}

/// <summary>Rate-limiter configuration. CONTRACT.md §8.</summary>
public sealed record RateLimitConfig
{
    /// <summary>Whether per-adapter rate limiting is active.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Behavior once an adapter's retry budget is spent.</summary>
    public RateLimitStrategy Strategy { get; init; } = RateLimitStrategy.Fallthrough;

    /// <summary>Extra retry attempts after the first failure.</summary>
    public int MaxRetryAttempts { get; init; } = 3;

    /// <summary>Initial backoff in seconds.</summary>
    public double InitialBackoffSeconds { get; init; } = 0.5;

    /// <summary>Maximum backoff in seconds.</summary>
    public double MaxBackoffSeconds { get; init; } = 30.0;

    /// <summary>Whether to apply jitter to backoff delays.</summary>
    public bool Jitter { get; init; } = true;

    /// <summary>Validate the configuration invariants.</summary>
    /// <exception cref="ArgumentException">When a value is out of range.</exception>
    public void Validate()
    {
        if (this.MaxRetryAttempts < 0)
        {
            throw new ArgumentException("MaxRetryAttempts must be >= 0");
        }

        if (this.InitialBackoffSeconds < 0 || this.MaxBackoffSeconds < this.InitialBackoffSeconds)
        {
            throw new ArgumentException("0 <= InitialBackoffSeconds <= MaxBackoffSeconds required");
        }
    }
}

/// <summary>Streaming configuration. CONTRACT.md §8.</summary>
public sealed record StreamingConfig
{
    /// <summary>Default polling interval in seconds.</summary>
    public double DefaultPollingIntervalSeconds { get; init; } = 4.0;

    /// <summary>Maximum reconnect attempts for native streams.</summary>
    public int MaxReconnectAttempts { get; init; } = 5;

    /// <summary>Reconnect backoff in seconds.</summary>
    public double ReconnectBackoffSeconds { get; init; } = 2.0;

    /// <summary>Validate the configuration invariants.</summary>
    /// <exception cref="ArgumentException">When a value is out of range.</exception>
    public void Validate()
    {
        if (this.DefaultPollingIntervalSeconds is < 1.0 or > 3600.0)
        {
            throw new ArgumentException("DefaultPollingIntervalSeconds must be 1.0..3600.0");
        }

        if (this.MaxReconnectAttempts < 0)
        {
            throw new ArgumentException("MaxReconnectAttempts must be >= 0");
        }

        if (this.ReconnectBackoffSeconds < 0)
        {
            throw new ArgumentException("ReconnectBackoffSeconds must be >= 0");
        }
    }
}

/// <summary>Orchestrator-wide configuration. CONTRACT.md §8.</summary>
public sealed record ClientConfig
{
    /// <summary>Cache configuration.</summary>
    public CacheConfig Cache { get; init; } = new();

    /// <summary>Rate-limiter configuration.</summary>
    public RateLimitConfig RateLimit { get; init; } = new();

    /// <summary>Streaming configuration.</summary>
    public StreamingConfig Streaming { get; init; } = new();

    /// <summary>Build a configuration with all-default sections.</summary>
    /// <returns>A fully defaulted configuration.</returns>
    public static ClientConfig Defaults() => new();

    /// <summary>Validate every nested configuration section.</summary>
    public void Validate()
    {
        this.Cache.Validate();
        this.RateLimit.Validate();
        this.Streaming.Validate();
    }
}
