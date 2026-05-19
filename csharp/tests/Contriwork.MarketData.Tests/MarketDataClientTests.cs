using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests;

/// <summary>Focused tests for MarketDataClient orchestration.</summary>
public sealed class MarketDataClientTests
{
    private static SpotPrice Spot(decimal last, string quote = "USDT") => new()
    {
        Symbol = "PLACEHOLDER",
        Last = last,
        QuoteCurrency = quote,
        Timestamp = DateTimeOffset.UnixEpoch,
        SourceAdapter = "PLACEHOLDER",
    };

    private static MarketDataClient Client(
        IReadOnlyDictionary<string, IReadOnlyList<IMarketDataAdapter>> chains)
        => new(new AdapterRegistry(chains), ClientConfig.Defaults(), new ManualClock());

    [Fact]
    public async Task GetSpot_Validates_Symbol()
    {
        var client = Client(new Dictionary<string, IReadOnlyList<IMarketDataAdapter>>
        {
            ["crypto"] = [new InMemoryAdapter("p")],
        });
        await Assert.ThrowsAsync<InvalidInputException>(
            () => client.GetSpotAsync(string.Empty, "crypto", "USDT"));
        await Assert.ThrowsAsync<InvalidInputException>(
            () => client.GetSpotAsync(new string('X', 65), "crypto", "USDT"));
        await Assert.ThrowsAsync<InvalidInputException>(
            () => client.GetSpotAsync("OK", "crypto", "U"));
    }

    [Fact]
    public async Task GetSpot_No_Adapter_For_Market()
    {
        var client = Client(new Dictionary<string, IReadOnlyList<IMarketDataAdapter>>
        {
            ["crypto"] = [new InMemoryAdapter("p")],
        });
        await Assert.ThrowsAsync<NoAdapterForMarketException>(
            () => client.GetSpotAsync("AAPL", "stocks_us"));
    }

    [Fact]
    public async Task GetSpot_Falls_Back_On_Primary_Failure()
    {
        var primary = new InMemoryAdapter(
            "primary",
            failModes: [new InMemoryFailMode("ETHUSDT", "SYMBOL_NOT_FOUND")]);
        var secondary = new InMemoryAdapter(
            "secondary",
            new Dictionary<string, InMemorySymbolData>
            {
                ["ETHUSDT"] = new() { Spot = Spot(3500m) },
            });
        var client = Client(new Dictionary<string, IReadOnlyList<IMarketDataAdapter>>
        {
            ["crypto"] = [primary, secondary],
        });
        var spot = await client.GetSpotAsync("ETHUSDT", "crypto", "USDT");
        Assert.Equal("secondary", spot.SourceAdapter);
    }

    [Fact]
    public async Task GetSpot_All_Adapters_Fail_Aggregates()
    {
        var a1 = new InMemoryAdapter("a1", failModes: [new InMemoryFailMode("X", "ADAPTER_UNAVAILABLE")]);
        var a2 = new InMemoryAdapter("a2", failModes: [new InMemoryFailMode("X", "ADAPTER_UNAVAILABLE")]);
        var client = Client(new Dictionary<string, IReadOnlyList<IMarketDataAdapter>>
        {
            ["crypto"] = [a1, a2],
        });
        var ex = await Assert.ThrowsAsync<AllAdaptersFailedException>(
            () => client.GetSpotAsync("X", "crypto", "USD"));
        Assert.Equal(2, ex.Causes.Count);
    }
}
