using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Finnhub adapter — US stocks. Uses <c>/api/v1/quote</c> and
/// <c>/api/v1/stock/candle</c>. Free tier ~60 req/min.
/// </summary>
public sealed class FinnhubAdapter : IMarketDataAdapter
{
    private static readonly Dictionary<Interval, string> ResolutionMap = new()
    {
        [Interval.M1] = "1",
        [Interval.M5] = "5",
        [Interval.M15] = "15",
        [Interval.M30] = "30",
        [Interval.H1] = "60",
        [Interval.D1] = "D",
        [Interval.W1] = "W",
        [Interval.MN1] = "M",
    };

    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="FinnhubAdapter"/> class.</summary>
    /// <param name="apiKey">Static API key.</param>
    /// <param name="apiKeyProvider">Lazy API key provider.</param>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public FinnhubAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.baseUrl = (baseUrl ?? "https://finnhub.io").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["stocks_us"],
            SupportedIntervals = [.. ResolutionMap.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Of("USD"),
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 60,
            RequiresAuth = true,
        };
    }

    /// <inheritdoc />
    public string AdapterId => "finnhub";

    /// <inheritdoc />
    public Capability Capability { get; }

    private async Task<string> KeyAsync(CancellationToken ct) =>
        (await AdapterHelpers.ResolveApiKeyAsync(
            this.AdapterId, this.apiKey, this.apiKeyProvider, required: true, ct)
            .ConfigureAwait(false))!;

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        var url = $"{this.baseUrl}/api/v1/quote" + HttpHelpers.QueryString(new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["token"] = key,
        });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("c", out var c)
            || c.GetDecimal() == 0m)
        {
            throw new SymbolNotFoundException($"finnhub has no quote for '{symbol}'", this.AdapterId);
        }

        return new SpotPrice
        {
            Symbol = symbol,
            Last = c.GetDecimal(),
            QuoteCurrency = quoteCurrency,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(
                root.TryGetProperty("t", out var t) ? t.GetInt64() : 0),
            SourceAdapter = this.AdapterId,
            High24h = OptDec(root, "h"),
            Low24h = OptDec(root, "l"),
            PreviousClose = OptDec(root, "pc"),
            Change24hPct = OptDec(root, "dp"),
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
        if (!ResolutionMap.TryGetValue(interval, out var resolution))
        {
            throw new InvalidIntervalException(
                $"finnhub does not support interval {interval}", this.AdapterId);
        }

        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        var to = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var from = since?.ToUnixTimeSeconds() ?? (to - (60L * 60 * 24 * 30));
        var url = $"{this.baseUrl}/api/v1/stock/candle" + HttpHelpers.QueryString(new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["resolution"] = resolution,
            ["from"] = from.ToString(CultureInfo.InvariantCulture),
            ["to"] = to.ToString(CultureInfo.InvariantCulture),
            ["token"] = key,
        });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("s", out var s)
            || s.GetString() != "ok")
        {
            throw new SymbolNotFoundException(
                $"finnhub has no candle data for '{symbol}'", this.AdapterId);
        }

        var ts = root.GetProperty("t");
        var opens = root.GetProperty("o");
        var highs = root.GetProperty("h");
        var lows = root.GetProperty("l");
        var closes = root.GetProperty("c");
        var volumes = root.GetProperty("v");
        var count = Math.Min(ts.GetArrayLength(), limit);

        var candles = new List<Candle>();
        for (var i = 0; i < count; i++)
        {
            candles.Add(new Candle
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(ts[i].GetInt64()),
                Open = opens[i].GetDecimal(),
                High = highs[i].GetDecimal(),
                Low = lows[i].GetDecimal(),
                Close = closes[i].GetDecimal(),
                Volume = volumes[i].GetDecimal(),
            });
        }

        return candles;
    }

    /// <inheritdoc />
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) =>
        throw new AdapterFeatureNotSupportedException(
            "finnhub does not expose order book on the free tier", this.AdapterId);

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
