namespace Contriwork.MarketData;

/// <summary>
/// Consumer-facing port — CONTRACT.md §2. The production implementation is
/// <see cref="MarketDataClient"/>; method names mirror the Python
/// (<c>snake_case</c>) and TypeScript (<c>camelCase</c>) ports.
/// </summary>
public interface IMarketDataPort
{
    /// <summary>Fetch the latest spot price for a symbol.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="market">Market string resolving to an adapter chain.</param>
    /// <param name="quoteCurrency">Quote currency; defaults to <c>USD</c>.</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>The latest <see cref="SpotPrice"/>.</returns>
    Task<SpotPrice> GetSpotAsync(
        string symbol,
        string market,
        string quoteCurrency = "USD",
        CancellationToken cancellationToken = default);

    /// <summary>Fetch historical OHLCV candles.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="market">Market string resolving to an adapter chain.</param>
    /// <param name="interval">Candle interval.</param>
    /// <param name="since">Lower-bound UTC timestamp; <c>null</c> for adapter default.</param>
    /// <param name="limit">Maximum candle count (1..1000).</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>Candles ordered ascending by timestamp.</returns>
    Task<IReadOnlyList<Candle>> GetOhlcvAsync(
        string symbol,
        string market,
        Interval interval,
        DateTimeOffset? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default);

    /// <summary>Fetch the top-N order book.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="market">Market string resolving to an adapter chain.</param>
    /// <param name="depth">Levels per side (1..100).</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>The <see cref="OrderBook"/>.</returns>
    Task<OrderBook> GetOrderBookAsync(
        string symbol,
        string market,
        int depth = 20,
        CancellationToken cancellationToken = default);

    /// <summary>Subscribe to a live ticker stream (native WSS or polling emulation).</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="market">Market string resolving to an adapter chain.</param>
    /// <param name="pollingFallback">Whether to emulate streaming via polling when no native feed exists.</param>
    /// <param name="pollingIntervalSeconds">Interval between polling requests (1..3600).</param>
    /// <param name="cancellationToken">Cancellation that stops the stream.</param>
    /// <returns>An async stream of <see cref="Ticker"/> updates.</returns>
    IAsyncEnumerable<Ticker> SubscribeTickerAsync(
        string symbol,
        string market,
        bool pollingFallback = true,
        double pollingIntervalSeconds = 4.0,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Adapter contract — CONTRACT.md §4. Each provider adapter implements this;
/// the orchestrator (<see cref="MarketDataClient"/>) layers cache, rate
/// limiting, and chain fallback on top.
/// </summary>
public interface IMarketDataAdapter
{
    /// <summary>Stable kebab-case adapter id (e.g. <c>"coingecko"</c>).</summary>
    string AdapterId { get; }

    /// <summary>Static description of what the adapter supports.</summary>
    Capability Capability { get; }

    /// <summary>Fetch the latest spot price.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="quoteCurrency">Quote currency.</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>The latest <see cref="SpotPrice"/>.</returns>
    Task<SpotPrice> GetSpotAsync(
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken);

    /// <summary>Fetch historical OHLCV candles.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="interval">Candle interval.</param>
    /// <param name="since">Lower-bound UTC timestamp, or <c>null</c>.</param>
    /// <param name="limit">Maximum candle count.</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>Candles ordered ascending by timestamp.</returns>
    Task<IReadOnlyList<Candle>> GetOhlcvAsync(
        string symbol,
        Interval interval,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>Fetch the top-N order book.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="depth">Levels per side.</param>
    /// <param name="cancellationToken">Cancellation for the operation.</param>
    /// <returns>The <see cref="OrderBook"/>.</returns>
    Task<OrderBook> GetOrderBookAsync(
        string symbol,
        int depth,
        CancellationToken cancellationToken);

    /// <summary>Open a native ticker stream for the symbol.</summary>
    /// <param name="symbol">Adapter-native symbol.</param>
    /// <param name="cancellationToken">Cancellation that stops the stream.</param>
    /// <returns>An async stream of <see cref="Ticker"/> updates.</returns>
    IAsyncEnumerable<Ticker> SubscribeTickerAsync(
        string symbol,
        CancellationToken cancellationToken);
}
