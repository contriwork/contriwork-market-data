using System.Collections.ObjectModel;

namespace Contriwork.MarketData;

/// <summary>
/// Shared default for the immutable <c>Extra</c> extension map.
/// </summary>
internal static class ExtraMap
{
    /// <summary>An empty, shared, read-only extension map.</summary>
    public static readonly IReadOnlyDictionary<string, object?> Empty =
        new ReadOnlyDictionary<string, object?>(new Dictionary<string, object?>());
}

/// <summary>
/// Latest spot price plus optional 24-hour statistics. CONTRACT v1 §5.1.
/// </summary>
public sealed record SpotPrice
{
    /// <summary>Adapter-native symbol the price is for.</summary>
    public required string Symbol { get; init; }

    /// <summary>Last traded price, in <see cref="QuoteCurrency"/>.</summary>
    public required decimal Last { get; init; }

    /// <summary>Currency the price is denominated in.</summary>
    public required string QuoteCurrency { get; init; }

    /// <summary>UTC timestamp the price was observed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Stable id of the adapter that produced the price.</summary>
    public required string SourceAdapter { get; init; }

    /// <summary>Best bid price, when the provider supplies it.</summary>
    public decimal? Bid { get; init; }

    /// <summary>Best ask price, when the provider supplies it.</summary>
    public decimal? Ask { get; init; }

    /// <summary>24-hour high.</summary>
    public decimal? High24h { get; init; }

    /// <summary>24-hour low.</summary>
    public decimal? Low24h { get; init; }

    /// <summary>24-hour traded volume.</summary>
    public decimal? Volume24h { get; init; }

    /// <summary>24-hour percentage change.</summary>
    public decimal? Change24hPct { get; init; }

    /// <summary>Market capitalization.</summary>
    public decimal? MarketCap { get; init; }

    /// <summary>Previous session close.</summary>
    public decimal? PreviousClose { get; init; }

    /// <summary>
    /// Provider-specific extension fields. Keys are namespaced
    /// <c>&lt;adapter_id&gt;.&lt;field&gt;</c>. Immutable.
    /// </summary>
    public IReadOnlyDictionary<string, object?> Extra { get; init; } = ExtraMap.Empty;
}

/// <summary>
/// A single OHLCV candle. CONTRACT v1 §5.2.
/// </summary>
public sealed record Candle
{
    /// <summary>UTC candle open time.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Open price.</summary>
    public required decimal Open { get; init; }

    /// <summary>High price.</summary>
    public required decimal High { get; init; }

    /// <summary>Low price.</summary>
    public required decimal Low { get; init; }

    /// <summary>Close price.</summary>
    public required decimal Close { get; init; }

    /// <summary>Base-asset traded volume.</summary>
    public required decimal Volume { get; init; }

    /// <summary>Quote-currency traded volume, when available.</summary>
    public decimal? QuoteVolume { get; init; }

    /// <summary>Number of trades in the candle, when available.</summary>
    public int? TradeCount { get; init; }

    /// <summary>Provider-specific extension fields.</summary>
    public IReadOnlyDictionary<string, object?> Extra { get; init; } = ExtraMap.Empty;
}

/// <summary>
/// A single price level in an order book. CONTRACT v1 §5.3.
/// </summary>
/// <param name="Price">Level price.</param>
/// <param name="Size">Level size.</param>
public readonly record struct BookLevel(decimal Price, decimal Size);

/// <summary>
/// Top-N order book. CONTRACT v1 §5.3.
/// </summary>
public sealed record OrderBook
{
    /// <summary>Adapter-native symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Bid levels, sorted descending by price.</summary>
    public required IReadOnlyList<BookLevel> Bids { get; init; }

    /// <summary>Ask levels, sorted ascending by price.</summary>
    public required IReadOnlyList<BookLevel> Asks { get; init; }

    /// <summary>UTC timestamp the book was observed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Stable id of the adapter that produced the book.</summary>
    public required string SourceAdapter { get; init; }

    /// <summary>Adapter-specific update sequence id, when available.</summary>
    public long? Sequence { get; init; }

    /// <summary>Provider-specific extension fields.</summary>
    public IReadOnlyDictionary<string, object?> Extra { get; init; } = ExtraMap.Empty;
}

/// <summary>
/// Side of a streamed ticker update. CONTRACT v1 §5.4.
/// </summary>
public enum TickerSide
{
    /// <summary>Update reflects a bid change.</summary>
    Bid,

    /// <summary>Update reflects an ask change.</summary>
    Ask,

    /// <summary>Update reflects a trade.</summary>
    Trade,
}

/// <summary>
/// A live ticker update. CONTRACT v1 §5.4.
/// </summary>
public sealed record Ticker
{
    /// <summary>Adapter-native symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Latest price.</summary>
    public required decimal Price { get; init; }

    /// <summary>Currency the price is denominated in.</summary>
    public required string QuoteCurrency { get; init; }

    /// <summary>UTC timestamp the update was observed.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Stable id of the adapter that produced the update.</summary>
    public required string SourceAdapter { get; init; }

    /// <summary>What the update reflects, when the provider distinguishes.</summary>
    public TickerSide? Side { get; init; }

    /// <summary>Trade or level size, when available.</summary>
    public decimal? Size { get; init; }

    /// <summary>Best bid, when available.</summary>
    public decimal? Bid { get; init; }

    /// <summary>Best ask, when available.</summary>
    public decimal? Ask { get; init; }

    /// <summary>Provider-specific extension fields.</summary>
    public IReadOnlyDictionary<string, object?> Extra { get; init; } = ExtraMap.Empty;
}

/// <summary>
/// Static description of what an adapter supports. CONTRACT v1 §5.7.
/// </summary>
public sealed record Capability
{
    /// <summary>Markets the adapter can serve.</summary>
    public required IReadOnlyList<string> SupportedMarkets { get; init; }

    /// <summary>Intervals the adapter supports for OHLCV.</summary>
    public required IReadOnlyList<Interval> SupportedIntervals { get; init; }

    /// <summary>
    /// Quote currencies the adapter supports, or the literal <c>"ANY"</c>
    /// when the adapter resolves quote currency dynamically.
    /// </summary>
    public required QuoteCurrencySupport SupportedQuoteCurrencies { get; init; }

    /// <summary>Whether the adapter implements <c>GetOrderBookAsync</c>.</summary>
    public required bool SupportsOrderBook { get; init; }

    /// <summary>Whether the adapter has a native streaming feed.</summary>
    public required bool SupportsNativeStreaming { get; init; }

    /// <summary>Default per-minute rate limit; caller may override via config.</summary>
    public required int RateLimitPerMinute { get; init; }

    /// <summary>Whether the adapter requires authentication.</summary>
    public required bool RequiresAuth { get; init; }

    /// <summary>Named tier options the adapter exposes, when applicable.</summary>
    public IReadOnlyList<string> TierOptions { get; init; } = [];
}

/// <summary>
/// Discriminated representation of <see cref="Capability.SupportedQuoteCurrencies"/>:
/// either an explicit set, or the dynamic <c>ANY</c> marker.
/// </summary>
public sealed record QuoteCurrencySupport
{
    private QuoteCurrencySupport(bool any, IReadOnlyList<string> currencies)
    {
        IsAny = any;
        Currencies = currencies;
    }

    /// <summary>Whether the adapter accepts any quote currency.</summary>
    public bool IsAny { get; }

    /// <summary>The explicit currency set; empty when <see cref="IsAny"/> is true.</summary>
    public IReadOnlyList<string> Currencies { get; }

    /// <summary>The dynamic "accepts anything" marker.</summary>
    public static QuoteCurrencySupport Any { get; } = new(true, []);

    /// <summary>Build an explicit-set capability from the given currencies.</summary>
    /// <param name="currencies">The supported currencies.</param>
    /// <returns>A capability covering exactly <paramref name="currencies"/>.</returns>
    public static QuoteCurrencySupport Of(params string[] currencies) =>
        new(false, currencies);

    /// <summary>Whether <paramref name="currency"/> is supported.</summary>
    /// <param name="currency">Currency code to test.</param>
    /// <returns><c>true</c> when supported.</returns>
    public bool Supports(string currency) =>
        IsAny || Currencies.Contains(currency, StringComparer.OrdinalIgnoreCase);
}
