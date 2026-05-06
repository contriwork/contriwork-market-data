using Xunit;

namespace Contriwork.MarketData.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Port_Interface_Is_Public()
    {
        var t = typeof(IMarketDataPort);
        Assert.True(t.IsPublic);
        Assert.True(t.IsInterface);
    }

    [Fact]
    public void Port_Declares_Example_Method()
    {
        var method = typeof(IMarketDataPort).GetMethod(nameof(IMarketDataPort.ExampleAsync));
        Assert.NotNull(method);
    }
}
