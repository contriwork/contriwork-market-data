using System.Runtime.CompilerServices;

namespace Contriwork.MarketData.Internal;

/// <summary>Polling-emulation helper for adapters without a native feed.</summary>
internal static class StreamingHelpers
{
    /// <summary>
    /// Yield a <see cref="Ticker"/> every <paramref name="pollingIntervalSeconds"/>
    /// by re-invoking the adapter's <c>GetSpotAsync</c>. After
    /// <paramref name="maxConsecutiveFailures"/> back-to-back failures, throws
    /// <see cref="StreamDisconnectedException"/>.
    /// </summary>
    /// <param name="adapter">Adapter to poll.</param>
    /// <param name="symbol">Symbol to poll.</param>
    /// <param name="quoteCurrency">Quote currency passed to <c>GetSpotAsync</c>.</param>
    /// <param name="pollingIntervalSeconds">Seconds between poll starts.</param>
    /// <param name="clock">Time source.</param>
    /// <param name="maxConsecutiveFailures">Failure budget before disconnecting.</param>
    /// <param name="cancellationToken">Cancellation that stops the stream.</param>
    /// <returns>An async stream of <see cref="Ticker"/> updates.</returns>
    public static async IAsyncEnumerable<Ticker> PollTickerAsync(
        IMarketDataAdapter adapter,
        string symbol,
        string quoteCurrency,
        double pollingIntervalSeconds,
        IClock clock,
        int maxConsecutiveFailures,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var failures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            SpotPrice? spot = null;
            try
            {
                spot = await adapter.GetSpotAsync(symbol, quoteCurrency, cancellationToken)
                    .ConfigureAwait(false);
                failures = 0;
            }
            catch (MarketDataException ex)
            {
                failures++;
                if (failures >= maxConsecutiveFailures)
                {
                    throw new StreamDisconnectedException(
                        $"polling emulation exhausted after {failures} consecutive failures "
                        + $"(last code={ex.Code})",
                        adapter.AdapterId);
                }
            }

            if (spot is not null)
            {
                yield return new Ticker
                {
                    Symbol = spot.Symbol,
                    Price = spot.Last,
                    QuoteCurrency = spot.QuoteCurrency,
                    Timestamp = spot.Timestamp,
                    SourceAdapter = spot.SourceAdapter,
                };
            }

            await clock.SleepAsync(pollingIntervalSeconds, cancellationToken).ConfigureAwait(false);
        }
    }
}
