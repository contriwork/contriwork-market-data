using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Coinbase Exchange public-data adapter. Uses <c>/products/{id}/ticker</c>,
/// <c>/stats</c>, <c>/candles</c>, and <c>/book</c>. Symbols use Coinbase
/// product IDs (<c>BTC-USD</c>).
/// </summary>
public sealed class CoinbaseAdapter : IMarketDataAdapter
{
    private static readonly IReadOnlyDictionary<Interval, int> IntervalMap = new Dictionary<Interval, int>
    {
        [Interval.M1] = 60,
        [Interval.M5] = 300,
        [Interval.M15] = 900,
        [Interval.H1] = 3600,
        [Interval.H4] = 21600,
        [Interval.D1] = 86400,
    };

    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="CoinbaseAdapter"/> class.</summary>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public CoinbaseAdapter(string? baseUrl = null, HttpClient? httpClient = null)
    {
        this.baseUrl = (baseUrl ?? "https://api.exchange.coinbase.com").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["crypto"],
            SupportedIntervals = [.. IntervalMap.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = true,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 600,
            RequiresAuth = false,
        };
    }

    /// <inheritdoc />
    public string AdapterId => "coinbase";

    /// <inheritdoc />
    public Capability Capability { get; }

    private async Task<JsonDocument> GetAsync(
        string path,
        IReadOnlyDictionary<string, string?>? query,
        CancellationToken ct)
    {
        var url = this.baseUrl + path + HttpHelpers.QueryString(query);
        try
        {
            return await HttpHelpers.GetJsonAsync(
                this.httpClient,
                this.AdapterId,
                url,
                new Dictionary<string, string> { ["Accept"] = "application/json" },
                ct).ConfigureAwait(false);
        }
        catch (AdapterUnavailableException ex)
            when (ex.Message.Contains("HTTP 404", StringComparison.Ordinal))
        {
            throw new SymbolNotFoundException(
                $"coinbase does not know product (404): {path}",
                this.AdapterId);
        }
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        using var ticker = await this.GetAsync($"/products/{symbol}/ticker", null, cancellationToken).ConfigureAwait(false);
        using var stats = await this.GetAsync($"/products/{symbol}/stats", null, cancellationToken).ConfigureAwait(false);

        var t = ticker.RootElement;
        if (t.ValueKind != JsonValueKind.Object || !t.TryGetProperty("price", out _))
        {
            throw new AdapterUnavailableException(
                "coinbase ticker returned unexpected payload", this.AdapterId);
        }

        var last = ParseDecimalString(t.GetProperty("price"));
        var timestamp = t.TryGetProperty("time", out var ts) && ts.ValueKind == JsonValueKind.String
            ? DateTimeOffset.Parse(ts.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
            : DateTimeOffset.UtcNow;

        return new SpotPrice
        {
            Symbol = symbol,
            Last = last,
            QuoteCurrency = quoteCurrency,
            Timestamp = timestamp,
            SourceAdapter = this.AdapterId,
            Bid = OptDecField(t, "bid"),
            Ask = OptDecField(t, "ask"),
            High24h = OptDecField(stats.RootElement, "high"),
            Low24h = OptDecField(stats.RootElement, "low"),
            Volume24h = OptDecField(stats.RootElement, "volume"),
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
        if (!IntervalMap.TryGetValue(interval, out var granularity))
        {
            throw new InvalidIntervalException(
                $"coinbase does not support interval {interval}", this.AdapterId);
        }

        var query = new Dictionary<string, string?>
        {
            ["granularity"] = granularity.ToString(CultureInfo.InvariantCulture),
        };
        if (since is { } s)
        {
            query["start"] = s.ToString("O", CultureInfo.InvariantCulture);
        }

        using var doc = await this.GetAsync($"/products/{symbol}/candles", query, cancellationToken).ConfigureAwait(false);
        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new AdapterUnavailableException(
                "coinbase candles returned unexpected payload", this.AdapterId);
        }

        var rows = doc.RootElement.EnumerateArray().Take(limit).Reverse().ToList();
        var candles = new List<Candle>();
        foreach (var row in rows)
        {
            var items = row.EnumerateArray().ToList();
            // [time, low, high, open, close, volume]
            candles.Add(new Candle
            {
                Timestamp = DateTimeOffset.FromUnixTimeSeconds(items[0].GetInt64()),
                Open = DecValue(items[3]),
                High = DecValue(items[2]),
                Low = DecValue(items[1]),
                Close = DecValue(items[4]),
                Volume = DecValue(items[5]),
            });
        }

        return candles;
    }

    /// <inheritdoc />
    public async Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken)
    {
        using var doc = await this.GetAsync(
            $"/products/{symbol}/book",
            new Dictionary<string, string?> { ["level"] = "2" },
            cancellationToken).ConfigureAwait(false);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterUnavailableException(
                "coinbase book returned unexpected payload", this.AdapterId);
        }

        return new OrderBook
        {
            Symbol = symbol,
            Bids = [.. ParseLevels(root.GetProperty("bids"), depth).OrderByDescending(b => b.Price)],
            Asks = [.. ParseLevels(root.GetProperty("asks"), depth).OrderBy(a => a.Price)],
            Timestamp = DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
            Sequence = root.TryGetProperty("sequence", out var seq) ? seq.GetInt64() : null,
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

    private static decimal ParseDecimalString(JsonElement el) =>
        el.ValueKind == JsonValueKind.String
            ? decimal.Parse(el.GetString()!, CultureInfo.InvariantCulture)
            : el.GetDecimal();

    private static decimal? OptDecField(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v) || v.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        var text = v.GetString();
        return string.IsNullOrEmpty(text) ? null : decimal.Parse(text, CultureInfo.InvariantCulture);
    }

    private static decimal DecValue(JsonElement el) =>
        el.ValueKind == JsonValueKind.String
            ? decimal.Parse(el.GetString()!, CultureInfo.InvariantCulture)
            : el.GetDecimal();
}
