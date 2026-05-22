using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for BinancePublicAdapter.</summary>
public sealed class BinancePublicAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/api/v3/ticker/24hr",
            """
            {
              "symbol": "BTCUSDT",
              "lastPrice": "65000.10",
              "bidPrice": "64999.99",
              "askPrice": "65000.21",
              "highPrice": "66000.00",
              "lowPrice": "64000.00",
              "quoteVolume": "1234567.89",
              "priceChangePercent": "1.45",
              "prevClosePrice": "64999.00",
              "closeTime": 1714492800000
            }
            """);
        using var client = new HttpClient(handler);
        var adapter = new BinancePublicAdapter(httpClient: client);

        var spot = await adapter.GetSpotAsync("BTCUSDT", "USDT", CancellationToken.None);
        Assert.Equal(65000.10m, spot.Last);
        Assert.Equal(64999.99m, spot.Bid);
        Assert.Equal("binance-public", spot.SourceAdapter);
    }

    [Fact]
    public async Task GetSpot_ErrorEnvelope_MapsToSymbolNotFound()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/api/v3/ticker/24hr", """{"code":-1121,"msg":"Invalid symbol."}""");
        using var client = new HttpClient(handler);
        var adapter = new BinancePublicAdapter(httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZUSDT", "USDT", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_ReturnsCandles()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/api/v3/klines",
            """
            [
              [1714492800000,"65000","65100","64900","65050","10.5",1714492859999,"682500",1234,"5.2","338000","0"],
              [1714492860000,"65050","65200","65000","65180","12.0",1714492919999,"780000",1500,"6.0","390000","0"]
            ]
            """);
        using var client = new HttpClient(handler);
        var adapter = new BinancePublicAdapter(httpClient: client);

        var candles = await adapter.GetOhlcvAsync("BTCUSDT", Interval.M1, null, 2, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.Equal(10.5m, candles[0].Volume);
        Assert.Equal(1234, candles[0].TradeCount);
    }

    [Fact]
    public async Task GetOrderBook_SortsLevels()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/api/v3/depth",
            """
            {"lastUpdateId":42,"bids":[["64999.0","1.0"],["64998.0","2.0"]],"asks":[["65001.0","1.0"],["65002.0","0.5"]]}
            """);
        using var client = new HttpClient(handler);
        var adapter = new BinancePublicAdapter(httpClient: client);

        var book = await adapter.GetOrderBookAsync("BTCUSDT", 2, CancellationToken.None);
        Assert.Equal(42, book.Sequence);
        Assert.True(book.Bids[0].Price > book.Bids[1].Price);
        Assert.True(book.Asks[0].Price < book.Asks[1].Price);
    }
}
