using System.Net;
using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for CoinGeckoAdapter.</summary>
public sealed class CoinGeckoAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/simple/price",
            """
            {
              "bitcoin": {
                "usd": 65000.5,
                "usd_24h_change": 1.23,
                "usd_24h_vol": 12345.6,
                "usd_market_cap": 1300000000000.0,
                "last_updated_at": 1714492800
              }
            }
            """);
        using var client = new HttpClient(handler);
        var adapter = new CoinGeckoAdapter(apiKey: "demo", httpClient: client);

        var spot = await adapter.GetSpotAsync("bitcoin", "USD", CancellationToken.None);
        Assert.Equal(65000.5m, spot.Last);
        Assert.Equal(1.23m, spot.Change24hPct);
        Assert.Equal("coingecko", spot.SourceAdapter);
    }

    [Fact]
    public async Task GetSpot_UnknownSymbol_Throws()
    {
        var handler = new FakeHttpMessageHandler().RespondTo("/simple/price", "{}");
        using var client = new HttpClient(handler);
        var adapter = new CoinGeckoAdapter(httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("nope", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetSpot_RateLimited()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/simple/price", "{}", HttpStatusCode.TooManyRequests);
        using var client = new HttpClient(handler);
        var adapter = new CoinGeckoAdapter(httpClient: client);

        await Assert.ThrowsAsync<RateLimitedException>(
            () => adapter.GetSpotAsync("bitcoin", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_FiltersBySince()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/coins/bitcoin/ohlc",
            "[[1714492800000,100,101,99,100],[1714496400000,100,102,99,101],[1714500000000,101,105,100,104]]");
        using var client = new HttpClient(handler);
        var adapter = new CoinGeckoAdapter(httpClient: client);

        var since = DateTimeOffset.FromUnixTimeSeconds(1714496400);
        var candles = await adapter.GetOhlcvAsync("bitcoin", Interval.H1, since, 100, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.All(candles, c => Assert.True(c.Timestamp >= since));
    }

    [Fact]
    public async Task GetOhlcv_InvalidInterval()
    {
        var adapter = new CoinGeckoAdapter();
        await Assert.ThrowsAsync<InvalidIntervalException>(
            () => adapter.GetOhlcvAsync("bitcoin", Interval.M5, null, 10, CancellationToken.None));
    }

    [Fact]
    public async Task OrderBook_NotSupported()
    {
        var adapter = new CoinGeckoAdapter();
        await Assert.ThrowsAsync<AdapterFeatureNotSupportedException>(
            () => adapter.GetOrderBookAsync("bitcoin", 20, CancellationToken.None));
    }

    [Fact]
    public async Task SubscribeTicker_NotSupported()
    {
        var adapter = new CoinGeckoAdapter();
        await Assert.ThrowsAsync<AdapterFeatureNotSupportedException>(async () =>
        {
            await foreach (var _ in adapter.SubscribeTickerAsync("bitcoin", CancellationToken.None))
            {
            }
        });
    }
}
