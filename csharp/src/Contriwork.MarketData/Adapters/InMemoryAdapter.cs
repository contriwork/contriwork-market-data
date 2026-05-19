using System.Runtime.CompilerServices;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Forces a specific error code for operations on a symbol. When
/// <see cref="FailFirstN"/> is set, the adapter fails only the first N calls
/// and succeeds afterwards — used for retry-then-success scenarios.
/// </summary>
public sealed class InMemoryFailMode
{
    private int remaining;

    /// <summary>Initializes a new instance of the <see cref="InMemoryFailMode"/> class.</summary>
    /// <param name="symbol">Symbol the fail mode applies to.</param>
    /// <param name="code">Error code to raise.</param>
    /// <param name="failFirstN">When set, fail only the first N calls.</param>
    public InMemoryFailMode(string symbol, string code, int? failFirstN = null)
    {
        // Throws KeyNotFoundException on a mis-spelled code — surfaces test errors early.
        _ = ErrorCodes.TypeForCode(code);
        this.Symbol = symbol;
        this.Code = code;
        this.FailFirstN = failFirstN;
        this.remaining = failFirstN ?? -1;
    }

    /// <summary>Symbol the fail mode applies to.</summary>
    public string Symbol { get; }

    /// <summary>Error code to raise.</summary>
    public string Code { get; }

    /// <summary>When set, fail only the first N calls.</summary>
    public int? FailFirstN { get; }

    /// <summary>Consume one invocation; returns whether this call should fail.</summary>
    /// <returns><c>true</c> when the call should raise.</returns>
    public bool Consume()
    {
        if (this.remaining == 0)
        {
            return false;
        }

        if (this.remaining > 0)
        {
            this.remaining--;
        }

        return true;
    }
}

/// <summary>Pre-seeded data for a single symbol on an <see cref="InMemoryAdapter"/>.</summary>
public sealed record InMemorySymbolData
{
    /// <summary>Spot price for the symbol, if any.</summary>
    public SpotPrice? Spot { get; init; }

    /// <summary>OHLCV candles keyed by interval.</summary>
    public IReadOnlyDictionary<Interval, IReadOnlyList<Candle>> Ohlcv { get; init; } =
        new Dictionary<Interval, IReadOnlyList<Candle>>();

    /// <summary>Order book for the symbol, if any.</summary>
    public OrderBook? OrderBook { get; init; }

    /// <summary>Native ticker stream events for the symbol.</summary>
    public IReadOnlyList<Ticker> TickerStream { get; init; } = [];
}

/// <summary>
/// Adapter backed by pre-seeded in-memory data. Drives the cross-language
/// contract-test fixtures and serves as the reference adapter implementation.
/// </summary>
public sealed class InMemoryAdapter : IMarketDataAdapter
{
    private readonly IReadOnlyDictionary<string, InMemorySymbolData> data;
    private readonly Dictionary<(string Symbol, string Code), InMemoryFailMode> failModes;
    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly IClock clock;
    private readonly Dictionary<string, int> callCounts = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="InMemoryAdapter"/> class.</summary>
    /// <param name="adapterId">Stable adapter id.</param>
    /// <param name="data">Pre-seeded per-symbol data.</param>
    /// <param name="capability">Capability; a permissive default when <c>null</c>.</param>
    /// <param name="failModes">Forced error programming.</param>
    /// <param name="apiKey">Static credential, if any.</param>
    /// <param name="apiKeyProvider">Lazy credential provider, if any.</param>
    /// <param name="clock">Time source; <see cref="SystemClock"/> when <c>null</c>.</param>
    public InMemoryAdapter(
        string adapterId,
        IReadOnlyDictionary<string, InMemorySymbolData>? data = null,
        Capability? capability = null,
        IEnumerable<InMemoryFailMode>? failModes = null,
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        IClock? clock = null)
    {
        if (string.IsNullOrEmpty(adapterId))
        {
            throw new ArgumentException("adapterId must be non-empty", nameof(adapterId));
        }

        this.AdapterId = adapterId;
        this.data = data ?? new Dictionary<string, InMemorySymbolData>(StringComparer.Ordinal);
        this.failModes = (failModes ?? [])
            .ToDictionary(fm => (fm.Symbol, fm.Code));
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.clock = clock ?? new SystemClock();
        this.Capability = capability ?? new Capability
        {
            SupportedMarkets = ["*"],
            SupportedIntervals = [.. Enum.GetValues<Interval>()],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = true,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 9999,
            RequiresAuth = false,
        };
    }

    /// <inheritdoc />
    public string AdapterId { get; }

    /// <inheritdoc />
    public Capability Capability { get; }

    /// <summary>Per-operation invocation counts — test introspection only.</summary>
    public IReadOnlyDictionary<string, int> CallCounts => this.callCounts;

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(
        string symbol,
        string quoteCurrency,
        CancellationToken cancellationToken)
    {
        await this.GateAsync("spot", symbol, quoteCurrency, cancellationToken).ConfigureAwait(false);
        var record = this.Symbol(symbol);
        if (record.Spot is null)
        {
            throw new SymbolNotFoundException(
                $"adapter {this.AdapterId} has no spot for symbol '{symbol}'",
                this.AdapterId);
        }

        return record.Spot with { Symbol = symbol, SourceAdapter = this.AdapterId };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Candle>> GetOhlcvAsync(
        string symbol,
        Interval interval,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken)
    {
        await this.GateAsync("ohlcv", symbol, quoteCurrency: null, cancellationToken)
            .ConfigureAwait(false);
        if (!this.Capability.SupportedIntervals.Contains(interval))
        {
            throw new InvalidIntervalException(
                $"adapter {this.AdapterId} does not support interval {interval}",
                this.AdapterId);
        }

        var record = this.Symbol(symbol);
        if (!record.Ohlcv.TryGetValue(interval, out var candles) || candles.Count == 0)
        {
            throw new SymbolNotFoundException(
                $"adapter {this.AdapterId} has no ohlcv for '{symbol}'/{interval}",
                this.AdapterId);
        }

        var filtered = candles
            .Where(c => since is null || c.Timestamp >= since)
            .OrderBy(c => c.Timestamp)
            .Take(limit)
            .ToList();
        return filtered;
    }

    /// <inheritdoc />
    public async Task<OrderBook> GetOrderBookAsync(
        string symbol,
        int depth,
        CancellationToken cancellationToken)
    {
        await this.GateAsync("order_book", symbol, quoteCurrency: null, cancellationToken)
            .ConfigureAwait(false);
        if (!this.Capability.SupportsOrderBook)
        {
            throw new AdapterFeatureNotSupportedException(
                $"adapter {this.AdapterId} does not support order book",
                this.AdapterId);
        }

        var record = this.Symbol(symbol);
        if (record.OrderBook is null)
        {
            throw new SymbolNotFoundException(
                $"adapter {this.AdapterId} has no order book for '{symbol}'",
                this.AdapterId);
        }

        return record.OrderBook with
        {
            Symbol = symbol,
            SourceAdapter = this.AdapterId,
            Bids = [.. record.OrderBook.Bids.OrderByDescending(b => b.Price).Take(depth)],
            Asks = [.. record.OrderBook.Asks.OrderBy(a => a.Price).Take(depth)],
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Ticker> SubscribeTickerAsync(
        string symbol,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await this.GateAsync("ticker", symbol, quoteCurrency: null, cancellationToken)
            .ConfigureAwait(false);
        var record = this.Symbol(symbol);
        foreach (var ticker in record.TickerStream)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return ticker with { Symbol = symbol, SourceAdapter = this.AdapterId };
        }
    }

    private async Task GateAsync(
        string op,
        string symbol,
        string? quoteCurrency,
        CancellationToken cancellationToken)
    {
        this.callCounts[op] = this.callCounts.GetValueOrDefault(op) + 1;

        if (this.Capability.RequiresAuth)
        {
            _ = await AdapterHelpers.ResolveApiKeyAsync(
                this.AdapterId,
                this.apiKey,
                this.apiKeyProvider,
                required: true,
                cancellationToken).ConfigureAwait(false);
        }

        if (quoteCurrency is not null
            && !this.Capability.SupportedQuoteCurrencies.Supports(quoteCurrency))
        {
            throw new UnsupportedQuoteCurrencyException(
                $"adapter {this.AdapterId} does not support quote currency '{quoteCurrency}'",
                this.AdapterId);
        }

        foreach (var fm in this.failModes.Values)
        {
            if (fm.Symbol == symbol && fm.Consume())
            {
                var exceptionType = ErrorCodes.TypeForCode(fm.Code);
                var message = $"adapter {this.AdapterId} forced {fm.Code} on symbol '{symbol}'";
                throw (MarketDataException)Activator.CreateInstance(
                    exceptionType,
                    message,
                    this.AdapterId)!;
            }
        }
    }

    private InMemorySymbolData Symbol(string symbol)
    {
        if (!this.data.TryGetValue(symbol, out var entry))
        {
            throw new SymbolNotFoundException(
                $"adapter {this.AdapterId} has no data for symbol '{symbol}'",
                this.AdapterId);
        }

        return entry;
    }
}
