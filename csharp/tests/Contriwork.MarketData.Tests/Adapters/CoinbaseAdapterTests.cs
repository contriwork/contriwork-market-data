using System.Net;
using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for CoinbaseAdapter.</summary>
public sealed class CoinbaseAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler()
            .RespondTo(
                "/products/BTC-USD/ticker",
                """
                {"price":"65000.10","bid":"64999.50","ask":"65000.50","time":"2026-04-30T12:00:00.000Z"}
                """)
            .RespondTo(
                "/products/BTC-USD/stats",
                """{"high":"66000","low":"64000","volume":"12345.6"}""");
        using var client = new HttpClient(handler);
        var adapter = new CoinbaseAdapter(httpClient: client);

        var spot = await adapter.GetSpotAsync("BTC-USD", "USD", CancellationToken.None);
        Assert.Equal(65000.10m, spot.Last);
        Assert.Equal(66000m, spot.High24h);
    }

    [Fact]
    public async Task GetSpot_404_MapsToSymbolNotFound()
    {
        var handler = new FakeHttpMessageHandler(); // unknown route → 404
        using var client = new HttpClient(handler);
        var adapter = new CoinbaseAdapter(httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZ-USD", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_ReversesToAscending()
    {
        // Coinbase returns descending by time.
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/products/BTC-USD/candles",
            "[[1714492920,65000,65200,65050,65180,12.0],[1714492860,64900,65100,65000,65050,10.5]]");
        using var client = new HttpClient(handler);
        var adapter = new CoinbaseAdapter(httpClient: client);

        var candles = await adapter.GetOhlcvAsync("BTC-USD", Interval.M1, null, 2, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.True(candles[0].Timestamp < candles[1].Timestamp);
    }
}
