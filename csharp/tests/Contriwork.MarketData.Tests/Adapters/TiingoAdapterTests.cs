using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for TiingoAdapter.</summary>
public sealed class TiingoAdapterTests
{
    [Fact]
    public async Task GetSpot_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/iex/AAPL",
            """
            [{"ticker":"AAPL","last":199.99,"tngoLast":199.99,"bidPrice":199.97,"askPrice":200.01,"high":200.10,"low":198.50,"volume":1234567,"prevClose":199.50,"timestamp":"2026-04-30T16:00:00.000Z"}]
            """);
        using var client = new HttpClient(handler);
        var adapter = new TiingoAdapter(apiKey: "test", httpClient: client);

        var spot = await adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None);
        Assert.Equal(199.99m, spot.Last);
        Assert.Equal("test", handler.LastHeaders!.Authorization?.Parameter);
    }

    [Fact]
    public async Task UnknownSymbol_EmptyList()
    {
        var handler = new FakeHttpMessageHandler().RespondTo("/iex/ZZZZ", "[]");
        using var client = new HttpClient(handler);
        var adapter = new TiingoAdapter(apiKey: "test", httpClient: client);

        await Assert.ThrowsAsync<SymbolNotFoundException>(
            () => adapter.GetSpotAsync("ZZZZ", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_Intraday()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "/iex/AAPL/prices",
            """
            [{"date":"2026-04-30T15:59:00.000Z","open":199,"high":200,"low":199,"close":199.5,"volume":5000},{"date":"2026-04-30T16:00:00.000Z","open":199.5,"high":200.1,"low":199.3,"close":200.0,"volume":6000}]
            """);
        using var client = new HttpClient(handler);
        var adapter = new TiingoAdapter(apiKey: "test", httpClient: client);

        var candles = await adapter.GetOhlcvAsync("AAPL", Interval.M1, null, 10, CancellationToken.None);
        Assert.Equal(2, candles.Count);
    }
}
