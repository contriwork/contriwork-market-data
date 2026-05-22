using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Tiingo adapter — US stocks via the IEX-routed feed. Uses <c>/iex/{ticker}</c>
/// and <c>/iex/{ticker}/prices</c>. Auth via <c>Authorization: Token</c>.
/// </summary>
public sealed class TiingoAdapter : IMarketDataAdapter
{
    private static readonly Dictionary<Interval, string> ResampleFreq = new()
    {
        [Interval.M1] = "1min",
        [Interval.M5] = "5min",
        [Interval.M15] = "15min",
        [Interval.M30] = "30min",
        [Interval.H1] = "1hour",
        [Interval.D1] = "daily",
    };

    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="TiingoAdapter"/> class.</summary>
    /// <param name="apiKey">Static API key.</param>
    /// <param name="apiKeyProvider">Lazy API key provider.</param>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public TiingoAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.baseUrl = (baseUrl ?? "https://api.tiingo.com").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["stocks_us"],
            SupportedIntervals = [.. ResampleFreq.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Of("USD"),
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 60,
            RequiresAuth = true,
        };
    }

    /// <inheritdoc />
    public string AdapterId => "tiingo";

    /// <inheritdoc />
    public Capability Capability { get; }

    private async Task<IReadOnlyDictionary<string, string>> HeadersAsync(CancellationToken ct)
    {
        var key = await AdapterHelpers.ResolveApiKeyAsync(
            this.AdapterId, this.apiKey, this.apiKeyProvider, required: true, ct).ConfigureAwait(false);
        return new Dictionary<string, string> { ["Authorization"] = $"Token {key}" };
    }

    private async Task<JsonDocument> GetAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken ct)
    {
        var url = this.baseUrl + path + HttpHelpers.QueryString(query);
        try
        {
            return await HttpHelpers.GetJsonAsync(
                this.httpClient, this.AdapterId, url, await this.HeadersAsync(ct).ConfigureAwait(false), ct)
                .ConfigureAwait(false);
        }
        catch (AdapterUnavailableException ex) when (ex.Message.Contains("HTTP 404", StringComparison.Ordinal))
        {
            throw new SymbolNotFoundException(
                $"tiingo does not know ticker (404): {path}", this.AdapterId);
        }
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        using var doc = await this.GetAsync($"/iex/{symbol}", query: null, cancellationToken).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            throw new SymbolNotFoundException($"tiingo returned no quote for '{symbol}'", this.AdapterId);
        }

        var item = doc.RootElement[0];
        var last = OptDec(item, "last") ?? OptDec(item, "tngoLast");
        if (last is null)
        {
            throw new SymbolNotFoundException($"tiingo returned empty quote for '{symbol}'", this.AdapterId);
        }

        return new SpotPrice
        {
            Symbol = symbol,
            Last = last.Value,
            QuoteCurrency = quoteCurrency,
            Timestamp = item.TryGetProperty("timestamp", out var ts) && ts.ValueKind == JsonValueKind.String
                ? ParseIso(ts.GetString()!)
                : DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
            Bid = OptDec(item, "bidPrice"),
            Ask = OptDec(item, "askPrice"),
            High24h = OptDec(item, "high"),
            Low24h = OptDec(item, "low"),
            Volume24h = OptDec(item, "volume"),
            PreviousClose = OptDec(item, "prevClose"),
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
        if (!ResampleFreq.TryGetValue(interval, out var freq))
        {
            throw new InvalidIntervalException(
                $"tiingo does not support interval {interval}", this.AdapterId);
        }

        var query = new Dictionary<string, string?> { ["resampleFreq"] = freq };
        if (since is { } s)
        {
            query["startDate"] = s.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        using var doc = await this.GetAsync($"/iex/{symbol}/prices", query, cancellationToken).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new AdapterUnavailableException("tiingo prices returned unexpected payload", this.AdapterId);
        }

        var candles = new List<Candle>();
        foreach (var row in doc.RootElement.EnumerateArray().Take(limit))
        {
            var dateText = row.TryGetProperty("date", out var d)
                ? d.GetString()
                : row.GetProperty("timestamp").GetString();
            candles.Add(new Candle
            {
                Timestamp = ParseIso(dateText!),
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
            "tiingo does not expose order book", this.AdapterId);

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);

    private static DateTimeOffset ParseIso(string text) =>
        DateTimeOffset.Parse(
            text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    private static decimal? OptDec(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return v.GetDecimal();
    }
}
