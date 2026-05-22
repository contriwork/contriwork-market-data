using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for KrakenAdapter.</summary>
public sealed class KrakenAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/0/public/Ticker",
            """
            {
              "error": [],
              "result": {
                "XXBTZUSD": {
                  "a": ["65010.00","1","1.000"],
                  "b": ["64990.00","1","1.000"],
                  "c": ["65000.00","0.50"],
                  "v": ["1.0","10.0"],
                  "p": ["64500.00","64750.00"],
                  "t": [5,100],
                  "l": ["64000.00","63500.00"],
                  "h": ["66000.00","66200.00"],
                  "o": "64800.00"
                }
              }
            }
            """);
        using var client = new HttpClient(handler);
        var adapter = new KrakenAdapter(httpClient: client);

        var spot = await adapter.GetSpotAsync("XXBTZUSD", "USD", CancellationToken.None);
        Assert.Equal(65000.00m, spot.Last);
        Assert.Equal(66200.00m, spot.High24h);
    }

    [Fact]
    public async Task UnknownPair_SymbolNotFound()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/0/public/Ticker",
            """{"error":["EQuery:Unknown asset pair"],"result":{}}""");
        using var client = new HttpClient(handler);
        var adapter = new KrakenAdapter(httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("NOPENOPE", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_ReturnsCandles()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/0/public/OHLC",
            """
            {
              "error": [],
              "result": {
                "XXBTZUSD": [
                  [1714492800,"65000.0","65100.0","64900.0","65050.0","65010.0","10.5",1234],
                  [1714492860,"65050.0","65200.0","65000.0","65180.0","65100.0","12.0",1500]
                ],
                "last": 1714492860
              }
            }
            """);
        using var client = new HttpClient(handler);
        var adapter = new KrakenAdapter(httpClient: client);

        var candles = await adapter.GetOhlcvAsync("XXBTZUSD", Interval.M1, null, 2, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.Equal(65050.0m, candles[0].Close);
        Assert.Equal(1234, candles[0].TradeCount);
    }
}
