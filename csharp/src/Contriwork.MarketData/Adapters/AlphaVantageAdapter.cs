using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// Alpha Vantage adapter — crypto + US stocks + BIST + forex. Uses
/// GLOBAL_QUOTE for stocks, CURRENCY_EXCHANGE_RATE for 3-char fiat/crypto
/// pairs, and TIME_SERIES_* for OHLCV. Free tier is throttled (5 req/min).
/// </summary>
public sealed class AlphaVantageAdapter : IMarketDataAdapter
{
    private static readonly Dictionary<Interval, string> IntradayInterval = new()
    {
        [Interval.M1] = "1min",
        [Interval.M5] = "5min",
        [Interval.M15] = "15min",
        [Interval.M30] = "30min",
        [Interval.H1] = "60min",
    };

    private static readonly Interval[] SupportedIntervalsList =
    [
        Interval.M1, Interval.M5, Interval.M15, Interval.M30, Interval.H1,
        Interval.D1, Interval.W1, Interval.MN1,
    ];

    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="AlphaVantageAdapter"/> class.</summary>
    /// <param name="apiKey">Static API key.</param>
    /// <param name="apiKeyProvider">Lazy API key provider.</param>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public AlphaVantageAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.baseUrl = (baseUrl ?? "https://www.alphavantage.co").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["crypto", "stocks_us", "stocks_tr", "forex"],
            SupportedIntervals = SupportedIntervalsList,
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 5,
            RequiresAuth = true,
            TierOptions = ["free", "premium"],
        };
    }

    /// <inheritdoc />
    public string AdapterId => "alpha-vantage";

    /// <inheritdoc />
    public Capability Capability { get; }

    private async Task<string> KeyAsync(CancellationToken ct) =>
        (await AdapterHelpers.ResolveApiKeyAsync(
            this.AdapterId, this.apiKey, this.apiKeyProvider, required: true, ct)
            .ConfigureAwait(false))!;

    private void CheckThrottle(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var text = string.Empty;
        if (root.TryGetProperty("Note", out var note))
        {
            text += note.GetString() ?? string.Empty;
        }

        if (root.TryGetProperty("Information", out var info))
        {
            text += info.GetString() ?? string.Empty;
        }

        var lower = text.ToLowerInvariant();
        if (lower.Contains("thank you for using", StringComparison.Ordinal)
            || lower.Contains("rate", StringComparison.Ordinal)
            || lower.Contains("throttle", StringComparison.Ordinal))
        {
            throw new RateLimitedException($"alpha-vantage throttled: {text}", this.AdapterId);
        }
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        var isPair = symbol.Contains('/', StringComparison.Ordinal)
            || (symbol.Length == 3 && symbol.All(char.IsLetter));

        if (isPair)
        {
            var fromCurrency = symbol.Split('/')[0];
            var pairUrl = $"{this.baseUrl}/query" + HttpHelpers.QueryString(new Dictionary<string, string?>
            {
                ["function"] = "CURRENCY_EXCHANGE_RATE",
                ["from_currency"] = fromCurrency,
                ["to_currency"] = quoteCurrency,
                ["apikey"] = key,
            });
            using var pairDoc = await HttpHelpers.GetJsonAsync(
                this.httpClient, this.AdapterId, pairUrl, headers: null, cancellationToken).ConfigureAwait(false);
            this.CheckThrottle(pairDoc.RootElement);
            if (!pairDoc.RootElement.TryGetProperty("Realtime Currency Exchange Rate", out var block))
            {
                throw new SymbolNotFoundException(
                    $"alpha-vantage has no exchange rate for '{symbol}'/'{quoteCurrency}'", this.AdapterId);
            }

            return new SpotPrice
            {
                Symbol = symbol,
                Last = DecField(block, "5. Exchange Rate"),
                QuoteCurrency = quoteCurrency,
                Timestamp = DateTimeOffset.UtcNow,
                SourceAdapter = this.AdapterId,
                Bid = OptDecField(block, "8. Bid Price"),
                Ask = OptDecField(block, "9. Ask Price"),
            };
        }

        var url = $"{this.baseUrl}/query" + HttpHelpers.QueryString(new Dictionary<string, string?>
        {
            ["function"] = "GLOBAL_QUOTE",
            ["symbol"] = symbol,
            ["apikey"] = key,
        });
        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        this.CheckThrottle(doc.RootElement);

        if (!doc.RootElement.TryGetProperty("Global Quote", out var quote)
            || !quote.TryGetProperty("05. price", out _))
        {
            throw new SymbolNotFoundException(
                $"alpha-vantage has no quote for '{symbol}'", this.AdapterId);
        }

        var changePct = quote.TryGetProperty("10. change percent", out var cp)
            ? cp.GetString()?.TrimEnd('%')
            : null;

        return new SpotPrice
        {
            Symbol = symbol,
            Last = DecField(quote, "05. price"),
            QuoteCurrency = quoteCurrency,
            Timestamp = DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
            High24h = OptDecField(quote, "03. high"),
            Low24h = OptDecField(quote, "04. low"),
            Volume24h = OptDecField(quote, "06. volume"),
            Change24hPct = string.IsNullOrEmpty(changePct)
                ? null
                : decimal.Parse(changePct, CultureInfo.InvariantCulture),
            PreviousClose = OptDecField(quote, "08. previous close"),
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
        if (!SupportedIntervalsList.Contains(interval))
        {
            throw new InvalidIntervalException(
                $"alpha-vantage does not support interval {interval}", this.AdapterId);
        }

        var key = await this.KeyAsync(cancellationToken).ConfigureAwait(false);
        string function;
        string seriesKey;
        var query = new Dictionary<string, string?> { ["symbol"] = symbol, ["apikey"] = key };

        if (IntradayInterval.TryGetValue(interval, out var avInterval))
        {
            function = "TIME_SERIES_INTRADAY";
            query["interval"] = avInterval;
            query["outputsize"] = "full";
            seriesKey = $"Time Series ({avInterval})";
        }
        else
        {
            (function, seriesKey) = interval switch
            {
                Interval.D1 => ("TIME_SERIES_DAILY", "Time Series (Daily)"),
                Interval.W1 => ("TIME_SERIES_WEEKLY", "Weekly Time Series"),
                _ => ("TIME_SERIES_MONTHLY", "Monthly Time Series"),
            };
        }

        query["function"] = function;
        var url = $"{this.baseUrl}/query" + HttpHelpers.QueryString(query);
        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient, this.AdapterId, url, headers: null, cancellationToken).ConfigureAwait(false);
        this.CheckThrottle(doc.RootElement);

        if (!doc.RootElement.TryGetProperty(seriesKey, out var series)
            || series.ValueKind != JsonValueKind.Object)
        {
            throw new SymbolNotFoundException(
                $"alpha-vantage has no time series for '{symbol}'/{interval}", this.AdapterId);
        }

        var candles = new List<Candle>();
        foreach (var dayProp in series.EnumerateObject())
        {
            var ts = ParseAlphaTimestamp(dayProp.Name);
            if (since is not null && ts < since)
            {
                continue;
            }

            var row = dayProp.Value;
            candles.Add(new Candle
            {
                Timestamp = ts,
                Open = DecField(row, "1. open"),
                High = DecField(row, "2. high"),
                Low = DecField(row, "3. low"),
                Close = DecField(row, "4. close"),
                Volume = row.TryGetProperty("5. volume", out var v)
                    ? decimal.Parse(v.GetString() ?? "0", CultureInfo.InvariantCulture)
                    : 0m,
            });
        }

        candles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return candles.Count > limit ? candles.GetRange(candles.Count - limit, limit) : candles;
    }

    /// <inheritdoc />
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) =>
        throw new AdapterFeatureNotSupportedException(
            "alpha-vantage does not expose order book", this.AdapterId);

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);

    private static DateTimeOffset ParseAlphaTimestamp(string text)
    {
        var raw = text.Trim();
        var format = raw.Contains(' ', StringComparison.Ordinal) ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd";
        return DateTimeOffset.ParseExact(
            raw, format, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
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
}
