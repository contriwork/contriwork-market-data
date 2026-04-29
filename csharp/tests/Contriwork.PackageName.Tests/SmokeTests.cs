using Xunit;

namespace Contriwork.PackageName.Tests;

public sealed class SmokeTests
{
    [Fact]
    public void Port_Interface_Is_Public()
    {
        var t = typeof(IPackageNamePort);
        Assert.True(t.IsPublic);
        Assert.True(t.IsInterface);
    }

    [Fact]
    public void Port_Declares_Example_Method()
    {
        var method = typeof(IPackageNamePort).GetMethod(nameof(IPackageNamePort.ExampleAsync));
        Assert.NotNull(method);
    }
}
