using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Kraken public-data adapter. Uses <c>/0/public/Ticker</c>, <c>/0/public/OHLC</c>,
/// and <c>/0/public/Depth</c>. Symbols use Kraken pair notation (<c>XXBTZUSD</c>).
/// </summary>
public sealed class KrakenAdapter : IMarketDataAdapter
{
    private static readonly IReadOnlyDictionary<Interval, int> IntervalMap = new Dictionary<Interval, int>
    {
        [Interval.M1] = 1,
        [Interval.M5] = 5,
        [Interval.M15] = 15,
        [Interval.M30] = 30,
        [Interval.H1] = 60,
        [Interval.H4] = 240,
        [Interval.D1] = 1440,
        [Interval.W1] = 10080,
        [Interval.MN1] = 21600,
    };

    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="KrakenAdapter"/> class.</summary>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public KrakenAdapter(string? baseUrl = null, HttpClient? httpClient = null)
    {
        this.baseUrl = (baseUrl ?? "https://api.kraken.com").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["crypto"],
            SupportedIntervals = [.. IntervalMap.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = true,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 60,
            RequiresAuth = false,
        };
    }

    /// <inheritdoc />
    public string AdapterId => "kraken";

    /// <inheritdoc />
    public Capability Capability { get; }

    private JsonElement CheckErrors(JsonElement root, string symbol)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterUnavailableException("kraken returned non-object payload", this.AdapterId);
        }

        if (root.TryGetProperty("error", out var errs) && errs.ValueKind == JsonValueKind.Array)
        {
            var joined = string.Join(";", errs.EnumerateArray().Select(e => e.GetString() ?? string.Empty));
            if (!string.IsNullOrEmpty(joined))
            {
                if (joined.Contains("Unknown asset pair", StringComparison.Ordinal)
                    || joined.Contains("Unknown asset", StringComparison.Ordinal))
                {
                    throw new SymbolNotFoundException(
                        $"kraken does not know symbol '{symbol}'", this.AdapterId);
                }

                throw new AdapterUnavailableException($"kraken error: {joined}", this.AdapterId);
            }
        }

        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterUnavailableException("kraken returned no result block", this.AdapterId);
        }

        return result;
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var url = $"{this.baseUrl}/0/public/Ticker"
            + HttpHelpers.QueryString(new Dictionary<string, string?> { ["pair"] = symbol });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        var result = this.CheckErrors(doc.RootElement, symbol);

        var first = result.EnumerateObject().FirstOrDefault();
        if (first.Value.ValueKind != JsonValueKind.Object)
        {
            throw new SymbolNotFoundException(
                $"kraken returned empty result for '{symbol}'", this.AdapterId);
        }

        var body = first.Value;
        return new SpotPrice
        {
            Symbol = symbol,
            Last = DecArrayHead(body, "c"),
            QuoteCurrency = quoteCurrency,
            Timestamp = DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
            Bid = DecArrayHead(body, "b"),
            Ask = DecArrayHead(body, "a"),
            High24h = DecArrayIndex(body, "h", 1),
            Low24h = DecArrayIndex(body, "l", 1),
            Volume24h = DecArrayIndex(body, "v", 1),
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
        if (!IntervalMap.TryGetValue(interval, out var minutes))
        {
            throw new InvalidIntervalException(
                $"kraken does not support interval {interval}", this.AdapterId);
        }

        var query = new Dictionary<string, string?>
        {
            ["pair"] = symbol,
            ["interval"] = minutes.ToString(CultureInfo.InvariantCulture),
        };
        if (since is { } s)
        {
            query["since"] = s.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        }

        var url = $"{this.baseUrl}/0/public/OHLC" + HttpHelpers.QueryString(query);

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        var result = this.CheckErrors(doc.RootElement, symbol);

        JsonElement rows = default;
        var found = false;
        foreach (var prop in result.EnumerateObject())
        {
            if (prop.Name == "last")
            {
                continue;
            }

            if (prop.Value.ValueKind == JsonValueKind.Array)
            {
                rows = prop.Value;
                found = true;
                break;
            }
        }

        if (!found || rows.GetArrayLength() == 0)
        {
            throw new SymbolNotFoundException(
                $"kraken returned no candles for '{symbol}'", this.AdapterId);
        }

        var candles = new List<Candle>();
        foreach (var row in rows.EnumerateArray().Take(limit))
        {
            var items = row.EnumerateArray().ToList();
            candles.Add(new Candle
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(items[0].GetInt64()),
                Open = DecValue(items[1]),
                High = DecValue(items[2]),
                Low = DecValue(items[3]),
                Close = DecValue(items[4]),
                Volume = DecValue(items[6]),
                TradeCount = items[7].GetInt32(),
            });
        }

        return candles;
    }

    /// <inheritdoc />
    public async Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken)
    {
        var url = $"{this.baseUrl}/0/public/Depth"
            + HttpHelpers.QueryString(new Dictionary<string, string?>
            {
                ["pair"] = symbol,
                ["count"] = Math.Min(depth, 500).ToString(CultureInfo.InvariantCulture),
            });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        var result = this.CheckErrors(doc.RootElement, symbol);

        var first = result.EnumerateObject().FirstOrDefault();
        if (first.Value.ValueKind != JsonValueKind.Object)
        {
            throw new SymbolNotFoundException(
                $"kraken returned no depth for '{symbol}'", this.AdapterId);
        }

        return new OrderBook
        {
            Symbol = symbol,
            Bids = [.. ParseLevels(first.Value.GetProperty("bids"), depth).OrderByDescending(b => b.Price)],
            Asks = [.. ParseLevels(first.Value.GetProperty("asks"), depth).OrderBy(a => a.Price)],
            Timestamp = DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
        };
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);

    private static List<BookLevel> ParseLevels(JsonElement array, int depth)
    {
        var result = new List<BookLevel>();
        foreach (var row in array.EnumerateArray().Take(depth))
        {
            var items = row.EnumerateArray().ToList();
            result.Add(new BookLevel(DecValue(items[0]), DecValue(items[1])));
        }

        return result;
    }

    private static decimal DecArrayHead(JsonElement body, string field) =>
        DecValue(body.GetProperty(field)[0]);

    private static decimal DecArrayIndex(JsonElement body, string field, int idx) =>
        DecValue(body.GetProperty(field)[idx]);

    private static decimal DecValue(JsonElement el) =>
        el.ValueKind == JsonValueKind.String
            ? decimal.Parse(el.GetString()!, CultureInfo.InvariantCulture)
            : el.GetDecimal();
}
