using System.Globalization;
using System.Runtime.CompilerServices;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData;

/// <summary>
/// Concrete <see cref="IMarketDataPort"/> implementation — CONTRACT.md §1, §3,
/// §4. Layers TTL cache, per-adapter token-bucket rate limiting, ordered
/// adapter fallback, and native-or-emulated streaming over a registry of
/// <see cref="IMarketDataAdapter"/> instances.
/// </summary>
public sealed class MarketDataClient : IMarketDataPort
{
    private const int OhlcvLimitCap = 1000;
    private const int OrderBookDepthCap = 100;
    private const int SymbolMaxLength = 64;

    // Codes that, when uniform across the entire chain, surface directly
    // instead of being wrapped in ALL_ADAPTERS_FAILED. CONTRACT.md §3.
    private static readonly HashSet<string> FatalPassthroughCodes = new(StringComparer.Ordinal)
    {
        InvalidIntervalException.ErrorCode,
        UnsupportedQuoteCurrencyException.ErrorCode,
        MissingCredentialsException.ErrorCode,
        SymbolNotFoundException.ErrorCode,
    };

    private readonly AdapterRegistry registry;
    private readonly ClientConfig config;
    private readonly IClock clock;
    private readonly TtlCache<SpotPrice> spotCache;
    private readonly TtlCache<IReadOnlyList<Candle>> ohlcvCache;
    private readonly TtlCache<OrderBook> orderBookCache;
    private readonly Dictionary<string, TokenBucket> buckets = new(StringComparer.Ordinal);

    /// <summary>Initializes a new instance of the <see cref="MarketDataClient"/> class.</summary>
    /// <param name="registry">Adapter registry.</param>
    /// <param name="config">Client configuration; defaults when <c>null</c>.</param>
    /// <param name="clock">Time source; <see cref="SystemClock"/> when <c>null</c>.</param>
    public MarketDataClient(AdapterRegistry registry, ClientConfig? config = null, IClock? clock = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        this.registry = registry;
        this.config = config ?? ClientConfig.Defaults();
        this.config.Validate();
        this.clock = clock ?? new SystemClock();
        this.spotCache = new TtlCache<SpotPrice>(this.config.Cache.MaxEntries, this.clock);
        this.ohlcvCache = new TtlCache<IReadOnlyList<Candle>>(this.config.Cache.MaxEntries, this.clock);
        this.orderBookCache = new TtlCache<OrderBook>(this.config.Cache.MaxEntries, this.clock);
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(
        string symbol,
        string market,
        string quoteCurrency = "USD",
        CancellationToken cancellationToken = default)
    {
        ValidateSymbol(symbol);
        ValidateMarket(market);
        ValidateQuoteCurrency(quoteCurrency);
        var chain = this.ResolveChain(market);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"get_spot|{market}|{symbol}|{quoteCurrency}");

        if (this.config.Cache.Enabled && this.spotCache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var result = await this.RunChainAsync(
            chain,
            adapter =>
            {
                RejectIfUnsupportedQuote(adapter, quoteCurrency);
                return adapter.GetSpotAsync(symbol, quoteCurrency, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);

        if (this.config.Cache.Enabled)
        {
            this.spotCache.Set(cacheKey, result, this.config.Cache.SpotTtlSeconds);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Candle>> GetOhlcvAsync(
        string symbol,
        string market,
        Interval interval,
        DateTimeOffset? since = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateSymbol(symbol);
        ValidateMarket(market);
        if (limit is < 1 or > OhlcvLimitCap)
        {
            throw new InvalidInputException($"limit must be 1..{OhlcvLimitCap}, got {limit}");
        }

        if (since is { } s && s > this.clock.UtcNow())
        {
            throw new InvalidInputException("since must not be in the future");
        }

        var chain = this.ResolveChain(market);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"get_ohlcv|{market}|{symbol}|{interval}|{since?.ToUnixTimeMilliseconds()}|{limit}");

        if (this.config.Cache.Enabled && this.ohlcvCache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var result = await this.RunChainAsync(
            chain,
            adapter =>
            {
                if (!adapter.Capability.SupportedIntervals.Contains(interval))
                {
                    throw new InvalidIntervalException(
                        $"adapter {adapter.AdapterId} does not support interval {interval}",
                        adapter.AdapterId);
                }

                return adapter.GetOhlcvAsync(symbol, interval, since, limit, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);

        if (this.config.Cache.Enabled)
        {
            this.ohlcvCache.Set(cacheKey, result, this.config.Cache.OhlcvTtlSeconds);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<OrderBook> GetOrderBookAsync(
        string symbol,
        string market,
        int depth = 20,
        CancellationToken cancellationToken = default)
    {
        ValidateSymbol(symbol);
        ValidateMarket(market);
        if (depth is < 1 or > OrderBookDepthCap)
        {
            throw new InvalidInputException($"depth must be 1..{OrderBookDepthCap}, got {depth}");
        }

        var chain = this.ResolveChain(market);
        var cacheKey = string.Create(
            CultureInfo.InvariantCulture,
            $"get_order_book|{market}|{symbol}|{depth}");

        if (this.config.Cache.Enabled && this.orderBookCache.TryGet(cacheKey, out var cached))
        {
            return cached;
        }

        var result = await this.RunChainAsync(
            chain,
            adapter =>
            {
                if (!adapter.Capability.SupportsOrderBook)
                {
                    throw new AdapterFeatureNotSupportedException(
                        $"adapter {adapter.AdapterId} does not support order book",
                        adapter.AdapterId);
                }

                return adapter.GetOrderBookAsync(symbol, depth, cancellationToken);
            },
            cancellationToken).ConfigureAwait(false);

        if (this.config.Cache.Enabled)
        {
            this.orderBookCache.Set(cacheKey, result, this.config.Cache.OrderBookTtlSeconds);
        }

        return result;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<Ticker> SubscribeTickerAsync(
        string symbol,
        string market,
        bool pollingFallback = true,
        double pollingIntervalSeconds = 4.0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ValidateSymbol(symbol);
        ValidateMarket(market);
        if (pollingIntervalSeconds is < 1.0 or > 3600.0)
        {
            throw new InvalidInputException(
                $"pollingIntervalSeconds must be 1.0..3600.0, got {pollingIntervalSeconds}");
        }

        var chain = this.ResolveChain(market);
        IMarketDataAdapter? native = null;
        IMarketDataAdapter? polling = null;
        foreach (var adapter in chain)
        {
            if (adapter.Capability.SupportsNativeStreaming)
            {
                native = adapter;
                break;
            }

            if (pollingFallback && polling is null)
            {
                polling = adapter;
            }
        }

        if (native is null && polling is null)
        {
            throw new StreamingNotSupportedException(
                $"no adapter in chain for market '{market}' supports streaming "
                + "(neither native nor polling fallback applies)");
        }

        if (native is not null)
        {
            await foreach (var ticker in native.SubscribeTickerAsync(symbol, cancellationToken)
                .ConfigureAwait(false))
            {
                yield return ticker;
            }

            yield break;
        }

        await foreach (var ticker in StreamingHelpers.PollTickerAsync(
            polling!,
            symbol,
            "USD",
            pollingIntervalSeconds,
            this.clock,
            maxConsecutiveFailures: 3,
            cancellationToken).ConfigureAwait(false))
        {
            yield return ticker;
        }
    }

    private static void ValidateSymbol(string symbol)
    {
        if (string.IsNullOrEmpty(symbol) || symbol.Length > SymbolMaxLength)
        {
            throw new InvalidInputException(
                $"symbol must be 1..{SymbolMaxLength} chars, got length "
                + (symbol?.Length.ToString(CultureInfo.InvariantCulture) ?? "null"));
        }

        if (!System.Text.Ascii.IsValid(symbol))
        {
            throw new InvalidInputException("symbol must be ASCII");
        }
    }

    private static void ValidateMarket(string market)
    {
        if (string.IsNullOrEmpty(market) || !System.Text.Ascii.IsValid(market))
        {
            throw new InvalidInputException("market must be a non-empty ASCII string");
        }
    }

    private static void ValidateQuoteCurrency(string quoteCurrency)
    {
        if (string.IsNullOrEmpty(quoteCurrency) || quoteCurrency.Length is < 2 or > 8)
        {
            throw new InvalidInputException(
                "quoteCurrency must be 2..8 chars, got length "
                + (quoteCurrency?.Length.ToString(CultureInfo.InvariantCulture) ?? "null"));
        }
    }

    private static void RejectIfUnsupportedQuote(IMarketDataAdapter adapter, string quoteCurrency)
    {
        if (!adapter.Capability.SupportedQuoteCurrencies.Supports(quoteCurrency))
        {
            throw new UnsupportedQuoteCurrencyException(
                $"adapter {adapter.AdapterId} does not support quote currency '{quoteCurrency}'",
                adapter.AdapterId);
        }
    }

    private IReadOnlyList<IMarketDataAdapter> ResolveChain(string market)
    {
        var chain = this.registry.ChainFor(market);
        if (chain.Count == 0)
        {
            throw new NoAdapterForMarketException(
                $"no adapter chain registered for market '{market}'");
        }

        return chain;
    }

    private TokenBucket? BucketFor(IMarketDataAdapter adapter)
    {
        if (!this.config.RateLimit.Enabled)
        {
            return null;
        }

        if (!this.buckets.TryGetValue(adapter.AdapterId, out var bucket))
        {
            var rpm = Math.Max(1, adapter.Capability.RateLimitPerMinute);
            bucket = new TokenBucket(rpm, rpm / 60.0, this.clock);
            this.buckets[adapter.AdapterId] = bucket;
        }

        return bucket;
    }

    private async Task<TResult> RunChainAsync<TResult>(
        IReadOnlyList<IMarketDataAdapter> chain,
        Func<IMarketDataAdapter, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        if (chain.Count == 1)
        {
            return await this.InvokeOneAsync(chain[0], operation, cancellationToken)
                .ConfigureAwait(false);
        }

        var causes = new List<MarketDataException>();
        foreach (var adapter in chain)
        {
            try
            {
                return await this.InvokeOneAsync(adapter, operation, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (RateLimitedException ex)
            {
                causes.Add(ex);
                if (this.config.RateLimit.Strategy == RateLimitStrategy.Bubble)
                {
                    throw;
                }
            }
            catch (MarketDataException ex)
                when (ex is AdapterFeatureNotSupportedException
                    or AdapterUnavailableException
                    or InvalidIntervalException
                    or MissingCredentialsException
                    or SymbolNotFoundException
                    or UnsupportedQuoteCurrencyException)
            {
                causes.Add(ex);
            }
        }

        var distinctCodes = causes.Select(c => c.Code).Distinct(StringComparer.Ordinal).ToList();
        if (distinctCodes.Count == 1 && FatalPassthroughCodes.Contains(distinctCodes[0]))
        {
            var first = causes[0];
            var message = $"all {causes.Count} adapter(s) failed with {first.Code}";
            throw (MarketDataException)Activator.CreateInstance(
                first.GetType(),
                message,
                first.AdapterId)!;
        }

        throw new AllAdaptersFailedException($"all {causes.Count} adapter(s) failed", causes);
    }

    private Task<TResult> InvokeOneAsync<TResult>(
        IMarketDataAdapter adapter,
        Func<IMarketDataAdapter, Task<TResult>> operation,
        CancellationToken cancellationToken)
    {
        var bucket = this.BucketFor(adapter);
        return RetryRunner.RunAsync(
            () => operation(adapter),
            this.config.RateLimit,
            this.clock,
            bucket,
            cancellationToken);
    }
}
