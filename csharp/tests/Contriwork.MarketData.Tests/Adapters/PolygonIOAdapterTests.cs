using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for PolygonIOAdapter.</summary>
public sealed class PolygonIOAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/v2/last/trade/AAPL",
            """{"status":"OK","results":{"p":199.99,"s":100,"t":1714492800000000000}}""");
        using var client = new HttpClient(handler);
        var adapter = new PolygonIOAdapter(apiKey: "test", httpClient: client);

        var spot = await adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None);
        Assert.Equal(199.99m, spot.Last);
    }

    [Fact]
    public async Task NotFound_MapsToSymbolNotFound()
    {
        var handler = new FakeHttpMessageHandler(); // 404
        using var client = new HttpClient(handler);
        var adapter = new PolygonIOAdapter(apiKey: "test", httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZZ", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_Aggs()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/v2/aggs/ticker/AAPL/range/1/day/",
            """
            {"status":"OK","results":[{"t":1714492800000,"o":199,"h":200,"l":198,"c":199.5,"v":1100000,"n":5000},{"t":1714579200000,"o":199.5,"h":200.5,"l":199,"c":200,"v":1200000,"n":6000}]}
            """);
        using var client = new HttpClient(handler);
        var adapter = new PolygonIOAdapter(apiKey: "test", httpClient: client);

        var candles = await adapter.GetOhlcvAsync("AAPL", Interval.D1, null, 10, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.Equal(5000, candles[0].TradeCount);
    }

    [Fact]
    public async Task OrderBook_NotSupported()
    {
        var adapter = new PolygonIOAdapter(apiKey: "test");
        await Assert.ThrowsAsync<AdapterFeatureNotSupportedException>(
            () => adapter.GetOrderBookAsync("AAPL", 10, CancellationToken.None));
    }
}
