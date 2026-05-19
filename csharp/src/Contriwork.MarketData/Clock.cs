namespace Contriwork.MarketData;

/// <summary>
/// Time source abstraction — injectable so cache TTL and rate-limit backoff
/// are deterministic under test. Production code uses <see cref="SystemClock"/>.
/// </summary>
public interface IClock
{
    /// <summary>Current UTC wall-clock time.</summary>
    /// <returns>The current instant.</returns>
    DateTimeOffset UtcNow();

    /// <summary>Monotonic seconds, suitable for measuring elapsed durations.</summary>
    /// <returns>A monotonically non-decreasing seconds value.</returns>
    double Monotonic();

    /// <summary>Asynchronously wait for the given duration.</summary>
    /// <param name="seconds">Seconds to sleep.</param>
    /// <param name="cancellationToken">Cancellation for the wait.</param>
    /// <returns>A task that completes after the delay.</returns>
    Task SleepAsync(double seconds, CancellationToken cancellationToken = default);
}

/// <summary>Wall-clock <see cref="IClock"/> backed by the runtime.</summary>
public sealed class SystemClock : IClock
{
    private static readonly long StartTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();

    /// <inheritdoc />
    public DateTimeOffset UtcNow() => DateTimeOffset.UtcNow;

    /// <inheritdoc />
    public double Monotonic() =>
        System.Diagnostics.Stopwatch.GetElapsedTime(StartTimestamp).TotalSeconds;

    /// <inheritdoc />
    public Task SleepAsync(double seconds, CancellationToken cancellationToken = default) =>
        seconds > 0
            ? Task.Delay(TimeSpan.FromSeconds(seconds), cancellationToken)
            : Task.CompletedTask;
}

/// <summary>
/// Test clock — the caller advances time explicitly. <see cref="SleepAsync"/>
/// advances the manual clock instead of waiting on the wall clock.
/// </summary>
public sealed class ManualClock : IClock
{
    private double monotonic;
    private DateTimeOffset now;

    /// <summary>Initializes a new instance of the <see cref="ManualClock"/> class.</summary>
    /// <param name="epochSeconds">Initial monotonic value and wall-clock epoch.</param>
    public ManualClock(double epochSeconds = 0.0)
    {
        this.monotonic = epochSeconds;
        this.now = DateTimeOffset.FromUnixTimeMilliseconds((long)(epochSeconds * 1000.0));
    }

    /// <inheritdoc />
    public DateTimeOffset UtcNow() => this.now;

    /// <inheritdoc />
    public double Monotonic() => this.monotonic;

    /// <summary>Advance the monotonic clock by <paramref name="seconds"/>.</summary>
    /// <param name="seconds">Non-negative seconds to advance.</param>
    public void Advance(double seconds)
    {
        if (seconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(seconds), "advance must be >= 0");
        }

        this.monotonic += seconds;
    }

    /// <summary>Set the wall-clock value returned by <see cref="UtcNow"/>.</summary>
    /// <param name="value">The new wall-clock instant.</param>
    public void SetNow(DateTimeOffset value) => this.now = value;

    /// <inheritdoc />
    public Task SleepAsync(double seconds, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        this.Advance(Math.Max(0.0, seconds));
        return Task.CompletedTask;
    }
}
