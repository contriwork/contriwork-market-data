using Contriwork.MarketData;
using Contriwork.MarketData.Internal;
using Xunit;

namespace Contriwork.MarketData.Tests;

/// <summary>Tests for the internal TtlCache and TokenBucket primitives.</summary>
public sealed class InternalPrimitivesTests
{
    [Fact]
    public void Cache_Hit_Within_Ttl()
    {
        var clock = new ManualClock();
        var cache = new TtlCache<string>(4, clock);
        cache.Set("a", "alpha", 5);
        clock.Advance(3);
        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal("alpha", value);
    }

    [Fact]
    public void Cache_Miss_After_Ttl()
    {
        var clock = new ManualClock();
        var cache = new TtlCache<string>(4, clock);
        cache.Set("a", "alpha", 5);
        clock.Advance(6);
        Assert.False(cache.TryGet("a", out _));
        Assert.Equal(0, cache.Count);
    }

    [Fact]
    public void Cache_Evicts_Least_Recently_Used()
    {
        var clock = new ManualClock();
        var cache = new TtlCache<string>(2, clock);
        cache.Set("a", "1", 100);
        cache.Set("b", "2", 100);
        _ = cache.TryGet("a", out _); // promote a
        cache.Set("c", "3", 100); // evicts b
        Assert.True(cache.TryGet("a", out _));
        Assert.False(cache.TryGet("b", out _));
        Assert.True(cache.TryGet("c", out _));
    }

    [Fact]
    public void Cache_Ignores_NonPositive_Ttl()
    {
        var clock = new ManualClock();
        var cache = new TtlCache<string>(4, clock);
        cache.Set("a", "alpha", 0);
        Assert.False(cache.TryGet("a", out _));
    }

    [Fact]
    public void TokenBucket_Drains_And_Refills()
    {
        var clock = new ManualClock();
        var bucket = new TokenBucket(2, refillPerSecond: 1.0, clock);
        Assert.True(bucket.TryAcquire());
        Assert.True(bucket.TryAcquire());
        Assert.False(bucket.TryAcquire());
        clock.Advance(1.5);
        Assert.True(bucket.TryAcquire());
    }

    [Fact]
    public void TokenBucket_TimeUntilAvailable()
    {
        var clock = new ManualClock();
        var bucket = new TokenBucket(1, refillPerSecond: 2.0, clock);
        _ = bucket.TryAcquire();
        var wait = bucket.TimeUntilAvailable();
        Assert.InRange(wait, 0.4, 0.6);
    }

    [Fact]
    public async Task RetryRunner_Succeeds_On_Second_Attempt()
    {
        var clock = new ManualClock();
        var config = new RateLimitConfig { MaxRetryAttempts = 3, InitialBackoffSeconds = 0.001, Jitter = false };
        var calls = 0;
        var result = await RetryRunner.RunAsync(
            () =>
            {
                calls++;
                return calls == 1
                    ? throw new RateLimitedException("first")
                    : Task.FromResult("ok");
            },
            config,
            clock,
            bucket: null,
            CancellationToken.None);
        Assert.Equal("ok", result);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task RetryRunner_Bubbles_When_Exhausted()
    {
        var clock = new ManualClock();
        var config = new RateLimitConfig { MaxRetryAttempts = 2, InitialBackoffSeconds = 0.001, Jitter = false };
        var calls = 0;
        await Assert.ThrowsAsync<RateLimitedException>(() => RetryRunner.RunAsync<string>(
            () =>
            {
                calls++;
                throw new RateLimitedException("always");
            },
            config,
            clock,
            bucket: null,
            CancellationToken.None));
        Assert.Equal(3, calls);
    }
}
