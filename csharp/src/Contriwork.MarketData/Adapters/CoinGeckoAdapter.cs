using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// CoinGecko REST adapter. Endpoints used:
/// <c>/simple/price</c> for spot and <c>/coins/{id}/ohlc</c> for candles.
/// Order book is not supported on the public REST tier.
/// </summary>
public sealed class CoinGeckoAdapter : IMarketDataAdapter
{
    private static readonly Dictionary<string, string> TierBaseUrl = new()
    {
        ["demo"] = "https://api.coingecko.com/api/v3",
        ["free"] = "https://api.coingecko.com/api/v3",
        ["pro"] = "https://pro-api.coingecko.com/api/v3",
    };

    private static readonly Dictionary<string, string?> TierAuthHeader = new()
    {
        ["demo"] = "x-cg-demo-api-key",
        ["free"] = null,
        ["pro"] = "x-cg-pro-api-key",
    };

    private static readonly Dictionary<Interval, string> DaysForInterval = new()
    {
        [Interval.M30] = "1",
        [Interval.H1] = "1",
        [Interval.H4] = "7",
        [Interval.D1] = "30",
        [Interval.W1] = "365",
    };

    private static readonly Dictionary<string, int> RateLimitPerMinuteByTier = new()
    {
        ["free"] = 10,
        ["demo"] = 30,
        ["pro"] = 500,
    };

    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string tier;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="CoinGeckoAdapter"/> class.</summary>
    /// <param name="apiKey">Static key, if any.</param>
    /// <param name="apiKeyProvider">Lazy key provider.</param>
    /// <param name="tier">Tier: <c>"demo"</c>, <c>"free"</c>, or <c>"pro"</c>.</param>
    /// <param name="baseUrl">Override the default endpoint base.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public CoinGeckoAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string tier = "demo",
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        if (!TierBaseUrl.TryGetValue(tier, out var defaultBase))
        {
            throw new ArgumentException(
                $"unknown tier '{tier}'; expected demo/free/pro",
                nameof(tier));
        }

        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.tier = tier;
        this.baseUrl = (baseUrl ?? defaultBase).TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["crypto"],
            SupportedIntervals = [.. DaysForInterval.Keys],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = RateLimitPerMinuteByTier[tier],
            RequiresAuth = tier == "pro",
            TierOptions = ["demo", "free", "pro"],
        };
    }

    /// <inheritdoc />
    public string AdapterId => "coingecko";

    /// <inheritdoc />
    public Capability Capability { get; }

    private async Task<IReadOnlyDictionary<string, string>?> BuildHeadersAsync(CancellationToken ct)
    {
        var headerName = TierAuthHeader[this.tier];
        if (headerName is null)
        {
            return null;
        }

        var key = await AdapterHelpers.ResolveApiKeyAsync(
            this.AdapterId,
            this.apiKey,
            this.apiKeyProvider,
            this.Capability.RequiresAuth,
            ct).ConfigureAwait(false);
        return string.IsNullOrEmpty(key)
            ? null
            : new Dictionary<string, string> { [headerName] = key };
    }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var vs = quoteCurrency.ToLowerInvariant();
        var url = this.baseUrl + "/simple/price" + HttpHelpers.QueryString(new Dictionary<string, string?>
        {
            ["ids"] = symbol,
            ["vs_currencies"] = vs,
            ["include_24hr_change"] = "true",
            ["include_24hr_vol"] = "true",
            ["include_market_cap"] = "true",
            ["include_last_updated_at"] = "true",
            ["precision"] = "full",
        });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient,
            this.AdapterId,
            url,
            await this.BuildHeadersAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Object
            || !doc.RootElement.TryGetProperty(symbol, out var body))
        {
            throw new SymbolNotFoundException(
                $"coingecko has no spot for '{symbol}'",
                this.AdapterId);
        }

        if (!body.TryGetProperty(vs, out var priceEl))
        {
            throw new AdapterUnavailableException(
                $"coingecko returned unexpected payload for '{symbol}'",
                this.AdapterId);
        }

        var timestamp = body.TryGetProperty("last_updated_at", out var ts)
            ? DateTimeOffset.FromUnixTimeSeconds(ts.GetInt64())
            : DateTimeOffset.UtcNow;

        return new SpotPrice
        {
            Symbol = symbol,
            Last = priceEl.GetDecimal(),
            QuoteCurrency = quoteCurrency,
            Timestamp = timestamp,
            SourceAdapter = this.AdapterId,
            Change24hPct = OptDecimal(body, $"{vs}_24h_change"),
            Volume24h = OptDecimal(body, $"{vs}_24h_vol"),
            MarketCap = OptDecimal(body, $"{vs}_market_cap"),
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
        if (!DaysForInterval.TryGetValue(interval, out var days))
        {
            throw new InvalidIntervalException(
                $"coingecko does not support interval {interval}",
                this.AdapterId);
        }

        var url = $"{this.baseUrl}/coins/{symbol}/ohlc"
            + HttpHelpers.QueryString(new Dictionary<string, string?>
            {
                ["vs_currency"] = "usd",
                ["days"] = days,
            });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient,
            this.AdapterId,
            url,
            await this.BuildHeadersAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken).ConfigureAwait(false);

        if (doc.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new SymbolNotFoundException(
                $"coingecko has no ohlcv for '{symbol}'",
                this.AdapterId);
        }

        var candles = new List<Candle>();
        foreach (var row in doc.RootElement.EnumerateArray())
        {
            var items = row.EnumerateArray().ToList();
            var ts = DateTimeOffset.FromUnixTimeMilliseconds(items[0].GetInt64());
            if (since is not null && ts < since)
            {
                continue;
            }

            candles.Add(new Candle
            {
                Timestamp = ts,
                Open = items[1].GetDecimal(),
                High = items[2].GetDecimal(),
                Low = items[3].GetDecimal(),
                Close = items[4].GetDecimal(),
                Volume = 0m,
            });
            if (candles.Count >= limit)
            {
                break;
            }
        }

        candles.Sort((a, b) => a.Timestamp.CompareTo(b.Timestamp));
        return candles;
    }

    /// <inheritdoc />
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) =>
        throw new AdapterFeatureNotSupportedException(
            "coingecko does not support order book",
            this.AdapterId);

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);

    private static decimal? OptDecimal(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return el.ValueKind == JsonValueKind.Number
            ? el.GetDecimal()
            : decimal.Parse(el.GetString() ?? "0", CultureInfo.InvariantCulture);
    }
}
