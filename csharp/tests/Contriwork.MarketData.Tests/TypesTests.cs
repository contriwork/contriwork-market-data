using Contriwork.MarketData;
using Xunit;

namespace Contriwork.MarketData.Tests;

/// <summary>Tests for the public data types.</summary>
public sealed class TypesTests
{
    [Fact]
    public void Interval_Order_Is_Invariant()
    {
        Assert.Equal(
            ["M1", "M5", "M15", "M30", "H1", "H4", "D1", "W1", "MN1"],
            Enum.GetNames<Interval>());
    }

    [Fact]
    public void SpotPrice_Extra_Defaults_To_Empty()
    {
        var spot = new SpotPrice
        {
            Symbol = "BTCUSDT",
            Last = 65000m,
            QuoteCurrency = "USDT",
            Timestamp = DateTimeOffset.UnixEpoch,
            SourceAdapter = "test",
        };
        Assert.Empty(spot.Extra);
    }

    [Fact]
    public void Records_Support_NonDestructive_Mutation()
    {
        var spot = new SpotPrice
        {
            Symbol = "BTCUSDT",
            Last = 65000m,
            QuoteCurrency = "USDT",
            Timestamp = DateTimeOffset.UnixEpoch,
            SourceAdapter = "old",
        };
        var rebound = spot with { SourceAdapter = "new" };
        Assert.Equal("old", spot.SourceAdapter);
        Assert.Equal("new", rebound.SourceAdapter);
    }

    [Fact]
    public void QuoteCurrencySupport_Any_Accepts_Everything()
    {
        Assert.True(QuoteCurrencySupport.Any.IsAny);
        Assert.True(QuoteCurrencySupport.Any.Supports("TRY"));
    }

    [Fact]
    public void QuoteCurrencySupport_Explicit_Set_Is_CaseInsensitive()
    {
        var support = QuoteCurrencySupport.Of("USD", "EUR");
        Assert.True(support.Supports("usd"));
        Assert.False(support.Supports("TRY"));
    }

    [Fact]
    public void BookLevel_Is_Value_Type()
    {
        var a = new BookLevel(100m, 1m);
        var b = new BookLevel(100m, 1m);
        Assert.Equal(a, b);
    }
}
