using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for FinnhubAdapter.</summary>
public sealed class FinnhubAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/api/v1/quote",
            """{"c":199.99,"h":200.10,"l":198.50,"o":199.50,"pc":199.50,"dp":0.24,"t":1714492800}""");
        using var client = new HttpClient(handler);
        var adapter = new FinnhubAdapter(apiKey: "test", httpClient: client);

        var spot = await adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None);
        Assert.Equal(199.99m, spot.Last);
        Assert.Equal(0.24m, spot.Change24hPct);
    }

    [Fact]
    public async Task UnknownSymbol_ZeroPrice()
    {
        var handler = new FakeHttpMessageHandler().RespondTo("/api/v1/quote", """{"c":0,"t":0}""");
        using var client = new HttpClient(handler);
        var adapter = new FinnhubAdapter(apiKey: "test", httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZZ", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_ReturnsCandles()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/api/v1/stock/candle",
            """{"s":"ok","c":[199.5,199.8],"h":[200.0,200.2],"l":[199.0,199.4],"o":[199.0,199.5],"t":[1714492800,1714492860],"v":[1000,1500]}""");
        using var client = new HttpClient(handler);
        var adapter = new FinnhubAdapter(apiKey: "test", httpClient: client);

        var candles = await adapter.GetOhlcvAsync("AAPL", Interval.M1, null, 10, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.Equal(199.5m, candles[0].Close);
    }

    [Fact]
    public async Task Ohlcv_NoData()
    {
        var handler = new FakeHttpMessageHandler().RespondTo("/api/v1/stock/candle", """{"s":"no_data"}""");
        using var client = new HttpClient(handler);
        var adapter = new FinnhubAdapter(apiKey: "test", httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetOhlcvAsync("AAPL", Interval.M1, null, 10, CancellationToken.None));
    }
}
