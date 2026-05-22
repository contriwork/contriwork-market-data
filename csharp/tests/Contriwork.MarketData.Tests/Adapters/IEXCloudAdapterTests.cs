using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for IEXCloudAdapter.</summary>
public sealed class IEXCloudAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/stable/stock/AAPL/quote",
            """
            {"symbol":"AAPL","latestPrice":199.99,"latestUpdate":1714492800000,"iexBidPrice":199.97,"iexAskPrice":200.01,"high":200.10,"low":198.50,"latestVolume":1234567,"changePercent":0.0024,"previousClose":199.50,"marketCap":3000000000000}
            """);
        using var client = new HttpClient(handler);
        var adapter = new IEXCloudAdapter(apiKey: "test", httpClient: client);

        var spot = await adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None);
        Assert.Equal(199.99m, spot.Last);
        Assert.Equal(3000000000000m, spot.MarketCap);
    }

    [Fact]
    public async Task NotFound_MapsToSymbolNotFound()
    {
        var handler = new FakeHttpMessageHandler(); // unknown route → 404
        using var client = new HttpClient(handler);
        var adapter = new IEXCloudAdapter(apiKey: "test", httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZZ", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_DailyChart()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/stable/stock/AAPL/chart/1m",
            """
            [{"date":"2026-04-29","open":199,"high":200,"low":198,"close":199.5,"volume":1100000},{"date":"2026-04-30","open":199.5,"high":200.5,"low":199,"close":200,"volume":1200000}]
            """);
        using var client = new HttpClient(handler);
        var adapter = new IEXCloudAdapter(apiKey: "test", httpClient: client);

        var candles = await adapter.GetOhlcvAsync("AAPL", Interval.D1, null, 10, CancellationToken.None);
        Assert.Equal(2, candles.Count);
    }

    [Fact]
    public async Task OrderBook_NotSupported()
    {
        var adapter = new IEXCloudAdapter(apiKey: "test");
        await Assert.ThrowsAsync<AdapterFeatureNotSupportedException>(
            () => adapter.GetOrderBookAsync("AAPL", 10, CancellationToken.None));
    }
}
