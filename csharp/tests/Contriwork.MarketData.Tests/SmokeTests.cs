using Contriwork.MarketData;
using Contriwork.MarketData.Adapters;
using Xunit;

namespace Contriwork.MarketData.Tests;

/// <summary>Smoke tests — verify the public surface is reachable.</summary>
public sealed class SmokeTests
{
    [Fact]
    public void Interval_Has_Nine_Members()
    {
        Assert.Equal(9, Enum.GetValues<Interval>().Length);
    }

    [Fact]
    public void Client_Constructs_With_Defaults()
    {
        var client = new MarketDataClient(new AdapterRegistry());
        Assert.IsAssignableFrom<IMarketDataPort>(client);
    }

    [Fact]
    public void InMemoryAdapter_Implements_Adapter_Contract()
    {
        var adapter = new InMemoryAdapter("test");
        Assert.IsAssignableFrom<IMarketDataAdapter>(adapter);
        Assert.Equal("test", adapter.AdapterId);
    }
}
