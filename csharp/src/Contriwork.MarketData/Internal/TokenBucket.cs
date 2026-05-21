using System.Security.Cryptography;

namespace Contriwork.MarketData.Internal;

/// <summary>
/// Classic refill token bucket. CONTRACT.md §6. Not thread-safe.
/// </summary>
internal sealed class TokenBucket
{
    private readonly int capacity;
    private readonly double refillPerSecond;
    private readonly IClock clock;
    private double tokens;
    private double last;

    /// <summary>Initializes a new instance of the <see cref="TokenBucket"/> class.</summary>
    /// <param name="capacity">Bucket capacity.</param>
    /// <param name="refillPerSecond">Tokens generated per second.</param>
    /// <param name="clock">Time source.</param>
    public TokenBucket(int capacity, double refillPerSecond, IClock clock)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "must be >= 1");
        }

        if (refillPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(refillPerSecond), "must be >= 0");
        }

        this.capacity = capacity;
        this.refillPerSecond = refillPerSecond;
        this.clock = clock;
        this.tokens = capacity;
        this.last = clock.Monotonic();
    }

    /// <summary>Attempt to take a token. Refills lazily before checking.</summary>
    /// <returns><c>true</c> when a token was available and consumed.</returns>
    public bool TryAcquire()
    {
        this.Refill();
        if (this.tokens + 1e-9 >= 1.0)
        {
            this.tokens -= 1.0;
            return true;
        }

        return false;
    }

    /// <summary>Seconds until a token becomes available.</summary>
    /// <returns>Zero when a token is ready; <see cref="double.PositiveInfinity"/> when the bucket never refills.</returns>
    public double TimeUntilAvailable()
    {
        this.Refill();
        var deficit = 1.0 - this.tokens;
        if (deficit <= 0)
        {
            return 0.0;
        }

        return this.refillPerSecond <= 0 ? double.PositiveInfinity : deficit / this.refillPerSecond;
    }

    private void Refill()
    {
        var now = this.clock.Monotonic();
        var elapsed = Math.Max(0.0, now - this.last);
        this.tokens = Math.Min(this.capacity, this.tokens + (elapsed * this.refillPerSecond));
        this.last = now;
    }
}

/// <summary>Rate-limit-aware retry runner. CONTRACT.md §6.</summary>
internal static class RetryRunner
{
    /// <summary>
    /// Invoke <paramref name="operation"/> with rate-limit-aware retry. On
    /// <see cref="RateLimitedException"/> the runner sleeps with jittered
    /// exponential backoff and retries up to <c>MaxRetryAttempts</c> times.
    /// </summary>
    /// <typeparam name="TResult">Operation result type.</typeparam>
    /// <param name="operation">The async operation to run.</param>
    /// <param name="config">Rate-limit configuration.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="bucket">Optional token bucket to queue on.</param>
    /// <param name="cancellationToken">Cancellation for the run.</param>
    /// <returns>The operation result.</returns>
    public static async Task<TResult> RunAsync<TResult>(
        Func<Task<TResult>> operation,
        RateLimitConfig config,
        IClock clock,
        TokenBucket? bucket,
        CancellationToken cancellationToken)
    {
        var attempts = 0;
        var backoff = Math.Max(0.0, config.InitialBackoffSeconds);
        while (true)
        {
            if (bucket is not null)
            {
                var wait = bucket.TimeUntilAvailable();
                if (wait > 0)
                {
                    if (double.IsPositiveInfinity(wait))
                    {
                        wait = config.MaxBackoffSeconds;
                    }

                    await clock.SleepAsync(wait, cancellationToken).ConfigureAwait(false);
                }

                bucket.TryAcquire();
            }

            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (RateLimitedException)
            {
                if (attempts >= config.MaxRetryAttempts)
                {
                    throw;
                }

                attempts++;
                var sleep = Math.Min(backoff, config.MaxBackoffSeconds);
                if (config.Jitter)
                {
                    sleep *= 0.5 + RandomNumberGenerator.GetInt32(0, 1_000_000) / 1_000_000.0;
                }

                await clock.SleepAsync(sleep, cancellationToken).ConfigureAwait(false);
                backoff = Math.Min(config.MaxBackoffSeconds, Math.Max(backoff, 0.001) * 2.0);
            }
        }
    }
}
