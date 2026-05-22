using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// IEX Cloud adapter — preserves the v1 path scheme (the service was sunset
/// in August 2024; callers running compatible mirrors can swap base_url).
/// Uses <c>/stable/stock/{symbol}/quote</c> and <c>/chart/{range}</c>.
/// </summary>
public sealed class IEXCloudAdapter : IMarketDataAdapter
{
    private static readonly Dictionary<Interval, string> RangeForInterval = new()
    {
        [Interval.D1] = "1m",
        [Interval.W1] = "1y",
        [Interval.MN1] = "max",
    };

    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="IEXCloudAdapter"/> class.</summary>
    /// <param name="apiKey">Static API key.</param>
    /// <param name="apiKeyProvider">Lazy API key provider.</param>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public IEXCloudAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.baseUrl = (baseUrl ?? "https://cloud.iexapis.com").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["stocks_us"],
            SupportedIntervals = [.. RangeForInterval.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Of("USD"),
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 100,
            RequiresAuth = true,
            TierOptions = ["sandbox", "standard"],
        };
    }

    /// <inheritdoc />
    public string AdapterId => "iex-cloud";

    /// <inheritdoc />
    public Capability Capability { get; }

    private async Task<string> KeyAsync(CancellationToken ct) =>
        (await AdapterHelpers.ResolveApiKeyAsync(
            this.AdapterId, this.apiKey, this.apiKeyProvider, required: true, ct)
            .ConfigureAwait(false))!;

    private async Task<JsonDocument> GetAsync(string path, IReadOnlyDictionary<string, string?> query, CancellationToken ct)
    {
        var url = this.baseUrl + path + HttpHelpers.QueryString(query);
        try
        {
            return await HttpHelpers.GetJsonAsync(this.httpClient, this.AdapterId, url, headers: null, ct)
                .ConfigureAwait(false);
        }
        catch (AdapterUnavailableException ex) when (ex.Message.Contains("HTTP 404", StringComparison.Ordinal))
        {
            throw new SymbolNotFoundException(
                $"iex-cloud does not know symbol (404): {path}", this.AdapterId);
        }
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await this.GetAsync(
            $"/stable/stock/{symbol}/quote",
            new Dictionary<string, string?> { ["token"] = key },
            cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("latestPrice", out var price))
        {
            throw new AdapterUnavailableException("iex-cloud returned unexpected payload", this.AdapterId);
        }

        return new SpotPrice
        {
            Symbol = symbol,
            Last = price.GetDecimal(),
            QuoteCurrency = quoteCurrency,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                root.TryGetProperty("latestUpdate", out var lu) ? lu.GetInt64() : 0),
            SourceAdapter = this.AdapterId,
            Bid = OptDec(root, "iexBidPrice") ?? OptDec(root, "bidPrice"),
            Ask = OptDec(root, "iexAskPrice") ?? OptDec(root, "askPrice"),
            High24h = OptDec(root, "high"),
            Low24h = OptDec(root, "low"),
            Volume24h = OptDec(root, "latestVolume"),
            Change24hPct = OptDec(root, "changePercent"),
            PreviousClose = OptDec(root, "previousClose"),
            MarketCap = OptDec(root, "marketCap"),
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Candle>> GetOhlcvAsync(
        string symbol,
        Interval interval,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken)
    {
        if (!RangeForInterval.TryGetValue(interval, out var range))
        {
            throw new InvalidIntervalException(
                $"iex-cloud does not support interval {interval}", this.AdapterId);
        }

        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await this.GetAsync(
            $"/stable/stock/{symbol}/chart/{range}",
            new Dictionary<string, string?> { ["token"] = key, ["chartCloseOnly"] = "false" },
            cancellationToken).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new AdapterUnavailableException("iex-cloud chart returned unexpected payload", this.AdapterId);
        }

        var candles = new List<Candle>();
        foreach (var row in doc.RootElement.EnumerateArray().Take(limit))
        {
            var ts = DateTimeOffset.ParseExact(
                row.GetProperty("date").GetString()!, "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
            if (since is not null && ts < since)
            {
                continue;
            }

            candles.Add(new Candle
            {
                Timestamp = ts,
                Open = row.GetProperty("open").GetDecimal(),
                High = row.GetProperty("high").GetDecimal(),
                Low = row.GetProperty("low").GetDecimal(),
                Close = row.GetProperty("close").GetDecimal(),
                Volume = row.TryGetProperty("volume", out var v) ? v.GetDecimal() : 0m,
            });
        }

        return candles;
    }

    /// <inheritdoc />
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) =>
        throw new AdapterFeatureNotSupportedException(
            "iex-cloud does not expose a public order book endpoint", this.AdapterId);

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);

    private static decimal? OptDec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return v.GetDecimal();
    }
}
