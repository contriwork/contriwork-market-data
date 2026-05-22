using System.Globalization;
using System.Text.Json;
using Contriwork.MarketData.Internal;

namespace Contriwork.MarketData.Adapters;

/// <summary>
/// CoinMarketCap Pro adapter. Only the latest-quote endpoint is wired in
/// v0.1.0 (free tier); historical OHLCV is paid-tier and out of scope.
/// Order book and streaming are not provided on supported tiers.
/// </summary>
public sealed class CoinMarketCapAdapter : IMarketDataAdapter
{
    private readonly string? apiKey;
    private readonly Func<CancellationToken, Task<string?>>? apiKeyProvider;
    private readonly string baseUrl;
    private readonly HttpClient httpClient;

    /// <summary>Initializes a new instance of the <see cref="CoinMarketCapAdapter"/> class.</summary>
    /// <param name="apiKey">Static API key.</param>
    /// <param name="apiKeyProvider">Lazy API key provider.</param>
    /// <param name="baseUrl">Override the default API host.</param>
    /// <param name="httpClient">Inject an <see cref="HttpClient"/> for testing.</param>
    public CoinMarketCapAdapter(
        string? apiKey = null,
        Func<CancellationToken, Task<string?>>? apiKeyProvider = null,
        string? baseUrl = null,
        HttpClient? httpClient = null)
    {
        this.apiKey = apiKey;
        this.apiKeyProvider = apiKeyProvider;
        this.baseUrl = (baseUrl ?? "https://pro-api.coinmarketcap.com").TrimEnd('/');
        this.httpClient = httpClient ?? new HttpClient();
        this.Capability = new Capability
        {
            SupportedMarkets = ["crypto"],
            SupportedIntervals = [],
            SupportedQuoteCurrencies = QuoteCurrencySupport.Any,
            SupportsOrderBook = false,
            SupportsNativeStreaming = false,
            RateLimitPerMinute = 30,
            RequiresAuth = true,
            TierOptions = ["basic", "hobbyist", "startup", "standard", "professional", "enterprise"],
        };
    }

    /// <inheritdoc />
    public string AdapterId => "coinmarketcap";

    /// <inheritdoc />
    public Capability Capability { get; }

    /// <inheritdoc />
    public async Task<SpotPrice> GetSpotAsync(string symbol, string quoteCurrency, CancellationToken cancellationToken)
    {
        var key = await AdapterHelpers.ResolveApiKeyAsync(
            this.AdapterId, this.apiKey, this.apiKeyProvider, required: true, cancellationToken)
            .ConfigureAwait(false);

        var url = $"{this.baseUrl}/v2/cryptocurrency/quotes/latest"
            + HttpHelpers.QueryString(new Dictionary<string, string?>
            {
                ["symbol"] = symbol,
                ["convert"] = quoteCurrency.ToUpperInvariant(),
            });

        using var doc = await HttpHelpers.GetJsonAsync(
            this.httpClient,
            this.AdapterId,
            url,
            new Dictionary<string, string>
            {
                ["X-CMC_PRO_API_KEY"] = key!,
                ["Accept"] = "application/json",
            },
            cancellationToken).ConfigureAwait(false);

        if (!doc.RootElement.TryGetProperty("data", out var data)
            || data.ValueKind != JsonValueKind.Object)
        {
            throw new SymbolNotFoundException(
                $"coinmarketcap does not know symbol '{symbol}'", this.AdapterId);
        }

        if (!data.TryGetProperty(symbol, out var entries)
            && !data.TryGetProperty(symbol.ToUpperInvariant(), out entries))
        {
            throw new SymbolNotFoundException(
                $"coinmarketcap does not know symbol '{symbol}'", this.AdapterId);
        }

        var entry = entries.ValueKind == JsonValueKind.Array
            ? entries.EnumerateArray().FirstOrDefault()
            : entries;

        if (!entry.TryGetProperty("quote", out var quoteBlock)
            || !quoteBlock.TryGetProperty(quoteCurrency.ToUpperInvariant(), out var quote))
        {
            throw new AdapterUnavailableException(
                $"coinmarketcap returned no quote for '{symbol}'/'{quoteCurrency}'",
                this.AdapterId);
        }

        return new SpotPrice
        {
            Symbol = symbol,
            Last = quote.GetProperty("price").GetDecimal(),
            QuoteCurrency = quoteCurrency,
            Timestamp = quote.TryGetProperty("last_updated", out var lu) && lu.ValueKind == JsonValueKind.String
                ? DateTimeOffset.Parse(lu.GetString()!, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal)
                : DateTimeOffset.UtcNow,
            SourceAdapter = this.AdapterId,
            Volume24h = OptDec(quote, "volume_24h"),
            Change24hPct = OptDec(quote, "percent_change_24h"),
            MarketCap = OptDec(quote, "market_cap"),
        };
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<Candle>> GetOhlcvAsync(
        string symbol,
        Interval interval,
        DateTimeOffset? since,
        int limit,
        CancellationToken cancellationToken) =>
        throw new InvalidIntervalException(
            "coinmarketcap historical OHLCV is paid-tier and out of v0.1.0 scope",
            this.AdapterId);

    /// <inheritdoc />
    public Task<OrderBook> GetOrderBookAsync(string symbol, int depth, CancellationToken cancellationToken) =>
        throw new AdapterFeatureNotSupportedException(
            "coinmarketcap does not expose order book on the supported tiers",
            this.AdapterId);

    /// <inheritdoc />
    public IAsyncEnumerable<Ticker> SubscribeTickerAsync(string symbol, CancellationToken cancellationToken) =>
        AdapterHelpers.StreamingNotSupportedAsync(this.AdapterId, cancellationToken);

    private static decimal? OptDec(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var el) || el.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return el.GetDecimal();
    }
}
