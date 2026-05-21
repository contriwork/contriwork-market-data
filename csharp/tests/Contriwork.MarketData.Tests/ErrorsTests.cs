using Contriwork.MarketData;
using Xunit;

namespace Contriwork.MarketData.Tests;

/// <summary>Tests for the locked error taxonomy.</summary>
public sealed class ErrorsTests
{
    [Theory]
    [InlineData("INVALID_INPUT", typeof(InvalidInputException))]
    [InlineData("INVALID_INTERVAL", typeof(InvalidIntervalException))]
    [InlineData("UNSUPPORTED_QUOTE_CURRENCY", typeof(UnsupportedQuoteCurrencyException))]
    [InlineData("SYMBOL_NOT_FOUND", typeof(SymbolNotFoundException))]
    [InlineData("RATE_LIMITED", typeof(RateLimitedException))]
    [InlineData("ADAPTER_UNAVAILABLE", typeof(AdapterUnavailableException))]
    [InlineData("ADAPTER_FEATURE_NOT_SUPPORTED", typeof(AdapterFeatureNotSupportedException))]
    [InlineData("MISSING_CREDENTIALS", typeof(MissingCredentialsException))]
    [InlineData("NO_ADAPTER_FOR_MARKET", typeof(NoAdapterForMarketException))]
    [InlineData("ALL_ADAPTERS_FAILED", typeof(AllAdaptersFailedException))]
    [InlineData("STREAMING_NOT_SUPPORTED", typeof(StreamingNotSupportedException))]
    [InlineData("STREAM_DISCONNECTED", typeof(StreamDisconnectedException))]
    public void Code_Maps_To_Exception_Type(string code, Type expected)
    {
        Assert.Equal(expected, ErrorCodes.TypeForCode(code));
    }

    [Fact]
    public void Unknown_Code_Throws()
    {
        Assert.Throws<KeyNotFoundException>(() => ErrorCodes.TypeForCode("NOPE"));
    }

    [Fact]
    public void AllAdaptersFailed_Preserves_Causes()
    {
        var causes = new MarketDataException[]
        {
            new AdapterUnavailableException("a", "x"),
            new AdapterUnavailableException("b", "y"),
        };
        var aggregate = new AllAdaptersFailedException("agg", causes);
        Assert.Equal(2, aggregate.Causes.Count);
    }

    [Fact]
    public void Subclasses_Carry_Stable_Code()
    {
        var ex = new RateLimitedException("limited", "binance");
        Assert.Equal("RATE_LIMITED", ex.Code);
        Assert.Equal("binance", ex.AdapterId);
    }
}
