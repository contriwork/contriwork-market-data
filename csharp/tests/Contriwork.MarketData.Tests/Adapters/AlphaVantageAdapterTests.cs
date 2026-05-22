using System.Net;
using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests.Adapters;

/// <summary>Unit tests for AlphaVantageAdapter.</summary>
public sealed class AlphaVantageAdapterTests
{
    [Fact]
    public async Task GlobalQuote_HappyPath()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "function=GLOBAL_QUOTE",
            """
            {"Global Quote":{"01. symbol":"AAPL","03. high":"200.10","04. low":"198.50","05. price":"199.99","06. volume":"1234567","08. previous close":"199.50","10. change percent":"0.24%"}}
            """);
        using var client = new HttpClient(handler);
        var adapter = new AlphaVantageAdapter(apiKey: "test", httpClient: client);

        var spot = await adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None);
        Assert.Equal(199.99m, spot.Last);
        Assert.Equal(0.24m, spot.Change24hPct);
    }

    [Fact]
    public async Task CurrencyExchangeRate_Path()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "function=CURRENCY_EXCHANGE_RATE",
            """
            {"Realtime Currency Exchange Rate":{"1. From_Currency Code":"BTC","3. To_Currency Code":"USD","5. Exchange Rate":"65000.0","8. Bid Price":"64990.0","9. Ask Price":"65010.0"}}
            """);
        using var client = new HttpClient(handler);
        var adapter = new AlphaVantageAdapter(apiKey: "test", httpClient: client);

        var spot = await adapter.GetSpotAsync("BTC", "USD", CancellationToken.None);
        Assert.Equal(65000.0m, spot.Last);
        Assert.Equal(64990.0m, spot.Bid);
    }

    [Fact]
    public async Task MissingCredentials_Throws()
    {
        var adapter = new AlphaVantageAdapter();
        await Assert.ThrowsAsync<MissingCredentialsException>(
            () => adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task ThrottleNote_RaisesRateLimited()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "function=GLOBAL_QUOTE",
            """{"Note":"Thank you for using Alpha Vantage! Our standard API rate limit is 5 calls per minute."}""");
        using var client = new HttpClient(handler);
        var adapter = new AlphaVantageAdapter(apiKey: "test", httpClient: client);

        await Assert.ThrowsAsync<RateLimitedException>(
            () => adapter.GetSpotAsync("AAPL", "USD", CancellationToken.None));
    }

    [Fact]
    public async Task GetOhlcv_Daily()
    {
        var handler = new FakeHttpMessageHandler().RespondTo(
            "function=TIME_SERIES_DAILY",
            """
            {"Time Series (Daily)":{"2026-04-30":{"1. open":"199.0","2. high":"200.0","3. low":"198.0","4. close":"199.5","5. volume":"1234567"},"2026-04-29":{"1. open":"198.0","2. high":"199.0","3. low":"197.0","4. close":"198.5","5. volume":"1100000"}}}
            """);
        using var client = new HttpClient(handler);
        var adapter = new AlphaVantageAdapter(apiKey: "test", httpClient: client);

        var candles = await adapter.GetOhlcvAsync("AAPL", Interval.D1, null, 100, CancellationToken.None);
        Assert.Equal(2, candles.Count);
        Assert.True(candles[0].Timestamp < candles[1].Timestamp);
    }
}
