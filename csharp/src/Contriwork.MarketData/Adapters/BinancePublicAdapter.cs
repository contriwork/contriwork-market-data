using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Binance public-data adapter — no API key required. Endpoints used:
/// <c>/api/v3/ticker/24hr</c>, <c>/api/v3/klines</c>, <c>/api/v3/depth</c>.
/// Symbols are pair strings (<c>BTCUSDT</c>).
/// </summary>
public sealed class BinancePublicAdapter : IMarketDataAdapter
{
    private static readonly IReadOnlyDictionary<Interval, string> IntervalMap = new Dictionary<Interval, string>
    {
        [Interval.M1] = "1m",
        [Interval.M5] = "5m",
        [Interval.M15] = "15m",
        [Interval.M30] = "30m",
        [Interval.H1] = "1h",
        [Interval.H4] = "4h",
        [Interval.D1] = "1d",
        [Interval.W1] = "1w",
        [Interval.MN1] = "1M",
    };

    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="BinancePublicAdapter"/> class.</summary>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public BinancePublicAdapter(string? baseUrl = null, HttpClient? httpClient = null)
    {
        this.baseUrl = (baseUrl ?? "https://api.binance.com").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["crypto"],
            SupportedIntervals = [.. IntervalMap.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = true,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 1000,
            RequiresAuth = false,
        };
    }

    /// <inheritdoc />
    public string AdapterId => "binance-public";

    /// <inheritdoc />
    public Capability Capability { get; }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var url = $"{this.baseUrl}/api/v3/ticker/24hr"
            + HttpHelpers.QueryString(new Dictionary<string, string?> { ["symbol"] = symbol });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);

        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterUnavailableException(
                "binance returned an unexpected payload type",
                this.AdapterId);
        }

        if (root.TryGetProperty("code", out var codeEl))
        {
            if (codeEl.GetInt32() == -1121)
            {
                throw new SymbolNotFoundException(
                    $"binance does not know symbol '{symbol}'", this.AdapterId);
            }

            var msg = root.TryGetProperty("msg", out var m) ? m.GetString() : "unknown";
            throw new AdapterUnavailableException(
                $"binance error {codeEl.GetInt32()}: {msg}", this.AdapterId);
        }

        return new SpotPrice
        {
            Symbol = symbol,
            Last = DecField(root, "lastPrice"),
            QuoteCurrency = quoteCurrency,
            Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(
                root.TryGetProperty("closeTime", out var ct) ? ct.GetInt64() : 0),
            SourceAdapter = this.AdapterId,
            Bid = OptDecField(root, "bidPrice"),
            Ask = OptDecField(root, "askPrice"),
            High24h = OptDecField(root, "highPrice"),
            Low24h = OptDecField(root, "lowPrice"),
            Volume24h = OptDecField(root, "quoteVolume"),
            Change24hPct = OptDecField(root, "priceChangePercent"),
            PreviousClose = OptDecField(root, "prevClosePrice"),
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
        if (!IntervalMap.TryGetValue(interval, out var binanceInterval))
        {
            throw new InvalidIntervalException(
                $"binance does not support interval {interval}", this.AdapterId);
        }

        var query = new Dictionary<string, string?>
        {
            ["symbol"] = symbol,
            ["interval"] = binanceInterval,
            ["limit"] = Math.Min(limit, 1000).ToString(CultureInfo.InvariantCulture),
        };
        if (since is { } s)
        {
            query["startTime"] = s.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
        }

        var url = $"{this.baseUrl}/api/v3/klines" + HttpHelpers.QueryString(query);

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("code", out var code)
            && code.GetInt32() == -1121)
        {
            throw new SymbolNotFoundException(
                $"binance does not know symbol '{symbol}'", this.AdapterId);
        }

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new AdapterUnavailableException(
                "binance klines returned unexpected payload", this.AdapterId);
        }

        var candles = new List<Candle>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var items = row.EnumerateArray().ToList();
            candles.Add(new Candle
            {
                Timestamp = DateTimeOffset.FromUnixTimeMilliseconds(items[0].GetInt64()),
                Open = DecValue(items[1]),
                High = DecValue(items[2]),
                Low = DecValue(items[3]),
                Close = DecValue(items[4]),
                Volume = DecValue(items[5]),
                QuoteVolume = DecValue(items[7]),
                TradeCount = items[8].GetInt32(),
            });
        }

        return candles;
    }

    /// <inheritdoc />
    public async Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken)
    {
        // Binance accepts 5/10/20/50/100; pick the smallest cap that fits.
        var caps = new[] { 5, 10, 20, 50, 100 };
        var binLimit = caps.FirstOrDefault(c => c >= depth, 100);

        var url = $"{this.baseUrl}/api/v3/depth"
            + HttpHelpers.QueryString(new Dictionary<string, string?>
            {
                ["symbol"] = symbol,
                ["limit"] = binLimit.ToString(CultureInfo.InvariantCulture),
            });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new AdapterUnavailableException(
                "binance depth returned unexpected payload", this.AdapterId);
        }

        if (doc.RootElement.TryGetProperty("code", out var code) && code.GetInt32() == -1121)
        {
            throw new SymbolNotFoundException(
                $"binance does not know symbol '{symbol}'", this.AdapterId);
        }

        var bids = ParseLevels(doc.RootElement.GetProperty("bids"), depth);
        var asks = ParseLevels(doc.RootElement.GetProperty("asks"), depth);

        return new OrderBook
        {
            Symbol = symbol,
            Bids = [.. bids.OrderByDescending(b => b.Price)],
            Asks = [.. asks.OrderBy(a => a.Price)],
            Timestamp = DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
            Sequence = doc.RootElement.TryGetProperty("lastUpdateId", out var seq) ? seq.GetInt64() : null,
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

    private static decimal DecField(JsonElement el, string name) =>
        decimal.Parse(el.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

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
