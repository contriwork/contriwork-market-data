using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Polygon.io adapter — US stocks + forex. Uses <c>/v2/last/trade/{ticker}</c>
/// and the aggregates endpoint. Free tier: 5 req/min.
/// </summary>
public sealed class PolygonIOAdapter : IMarketDataAdapter
{
    private static readonly Dictionary<Interval, (int Multiplier, string Span)> IntervalMap = new()
    {
        [Interval.M1] = (1, "minute"),
        [Interval.M5] = (5, "minute"),
        [Interval.M15] = (15, "minute"),
        [Interval.M30] = (30, "minute"),
        [Interval.H1] = (1, "hour"),
        [Interval.H4] = (4, "hour"),
        [Interval.D1] = (1, "day"),
        [Interval.W1] = (1, "week"),
        [Interval.MN1] = (1, "month"),
    };

    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="PolygonIOAdapter"/> class.</summary>
    /// <param name="apiKey">Static API key.</param>
    /// <param name="apiKeyProvider">Lazy API key provider.</param>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public PolygonIOAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.baseUrl = (baseUrl ?? "https://api.polygon.io").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["stocks_us", "forex"],
            SupportedIntervals = [.. IntervalMap.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Of("USD"),
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 5,
            RequiresAuth = true,
        };
    }

    /// <inheritdoc />
    public string AdapterId => "polygon-io";

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
                $"polygon-io does not know ticker (404): {path}", this.AdapterId);
        }
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        using var doc = await this.GetAsync(
            $"/v2/last/trade/{symbol}",
            new Dictionary<string, string?> { ["apiKey"] = key },
            cancellationToken).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty("results", out var results))
        {
            throw new AdapterUnavailableException("polygon-io trade returned unexpected payload", this.AdapterId);
        }

        if (!results.TryGetProperty("p", out var p))
        {
            throw new SymbolNotFoundException(
                $"polygon-io returned no last trade for '{symbol}'", this.AdapterId);
        }

        var tsNs = results.TryGetProperty("t", out var t) ? t.GetInt64() : 0L;
        return new SpotPrice
        {
            Symbol = symbol,
            Last = p.GetDecimal(),
            QuoteCurrency = quoteCurrency,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(tsNs / 1_000_000),
            SourceAdapter = this.AdapterId,
            Volume24h = results.TryGetProperty("s", out var s) ? s.GetDecimal() : null,
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
        if (!IntervalMap.TryGetValue(interval, out var spec))
        {
            throw new InvalidIntervalException(
                $"polygon-io does not support interval {interval}", this.AdapterId);
        }

        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        var end = DateTimeOffset.UtcNow;
        var start = since ?? end.AddYears(-1);
        var path = $"/v2/aggs/ticker/{symbol}/range/{spec.Multiplier}/{spec.Span}/"
            + $"{start:yyyy-MM-dd}/{end:yyyy-MM-dd}";
        using var doc = await this.GetAsync(
            path,
            new Dictionary<string, string?>
            {
                ["apiKey"] = key,
                ["limit"] = Math.Min(limit, 5000).ToString(CultureInfo.InvariantCulture),
            },
            cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterUnavailableException("polygon-io aggs returned unexpected payload", this.AdapterId);
        }

        var candles = new List<Candle>();
        if (doc.RootElement.TryGetProperty("results", out var results)
            && results.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in results.EnumerateArray().Take(limit))
            {
                candles.Add(new Candle
                {
                    Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                        row.TryGetProperty("t", out var t) ? t.GetInt64() : 0),
                    Open = row.GetProperty("o").GetDecimal(),
                    High = row.GetProperty("h").GetDecimal(),
                    Low = row.GetProperty("l").GetDecimal(),
                    Close = row.GetProperty("c").GetDecimal(),
                    Volume = row.TryGetProperty("v", out var v) ? v.GetDecimal() : 0m,
                    TradeCount = row.TryGetProperty("n", out var n) ? n.GetInt32() : null,
                });
            }
        }

        return candles;
    }

    /// <inheritdoc />
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) =>
        throw new AdapterFeatureNotSupportedException(
            "polygon-io order book requires the L2 paid tier and is out of v0.1.0 scope",
            this.AdapterId);

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);
}
