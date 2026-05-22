using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for CoinMarketCapAdapter.</summary>
public sealed class CoinMarketCapAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/v2/cryptocurrency/quotes/latest",
            """
            {
              "data": {
                "BTC": [
                  {
                    "id": 1,
                    "name": "Bitcoin",
                    "symbol": "BTC",
                    "quote": {
                      "USD": {
                        "price": 65000.12,
                        "volume_24h": 12345.6,
                        "percent_change_24h": 1.23,
                        "market_cap": 1.3e12,
                        "last_updated": "2026-04-30T12:00:00.000Z"
                      }
                    }
                  }
                ]
              }
            }
            """);
        using var client = new HttpClient(handler);
        var adapter = new CoinMarketCapAdapter(apiKey: "test-key", httpClient: client);

        var spot = await adapter.GetSpotAsync("BTC", "USD", CancellationToken.None);
        Assert.Equal(65000.12m, spot.Last);
        Assert.Equal(1.23m, spot.Change24hPct);
        Assert.Equal("test-key", handler.LastHeaders!.GetValues("X-CMC_PRO_API_KEY").First());
    }

    [Fact]
    public async Task MissingCredentials_Throws()
    {
        var adapter = new CoinMarketCapAdapter();
        await Assert.ThrowsAsync<MissingCredentialsException>(
            () => adapter.GetSpotAsync("BTC", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task UnknownSymbol()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/v2/cryptocurrency/quotes/latest", """{"data":{}}""");
        using var client = new HttpClient(handler);
        var adapter = new CoinMarketCapAdapter(apiKey: "k", httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZ", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_PaidTier_Throws()
    {
        var adapter = new CoinMarketCapAdapter(apiKey: "k");
        await Assert.ThrowsAsync<InvalidIntervalException>(
            () => adapter.GetOhlcvAsync("BTC", Interval.D1, null, 30, CancellationToken.None));
    }

    [Fact]
    public async Task GetOrderBook_NotSupported()
    {
        var adapter = new CoinMarketCapAdapter(apiKey: "k");
        await Assert.ThrowsAsync<AdapterFeatureNotSupportedException>(
            () => adapter.GetOrderBookAsync("BTC", 10, CancellationToken.None));
    }

    [Fact]
    public async Task ApiKeyProvider_Overrides_Static_Key()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/v2/cryptocurrency/quotes/latest",
            """
            {"data":{"BTC":[{"id":1,"quote":{"USD":{"price":65000.0,"last_updated":"2026-04-30T12:00:00.000Z"}}}]}}
            """);
        using var client = new HttpClient(handler);
        var adapter = new CoinMarketCapAdapter(
            apiKey: "static",
            apiKeyProvider: _ => Task.FromResult<string?>("provider-key"),
            httpClient: client);

        await adapter.GetSpotAsync("BTC", "USD", CancellationToken.None);
        Assert.Equal("provider-key", handler.LastHeaders!.GetValues("X-CMC_PRO_API_KEY").First());
    }
}
