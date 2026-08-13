using Microsoft.AspNetCore.Hosting;

namespace Carina.Api.Tests.FeatureTest;

public sealed class StartupValidationTests
{
    [Fact]
    public void StartupFailsWhenTheConnectionStringIsMissing()
    {
        using var factory = new TestingWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Carina", ""));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("ConnectionStrings:Carina", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailsWhenTheDriverSocketPathIsMissing()
    {
        using var factory = new TestingWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("CARINA_DRIVER_SOCKET", ""));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("CARINA_DRIVER_SOCKET", exception.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailsWhenTheDriverSocketPathIsRelative()
    {
        using var factory = new TestingWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("CARINA_DRIVER_SOCKET", "driver.sock"));

        var exception = Record.Exception(() => factory.CreateClient());

        Assert.NotNull(exception);
        Assert.Contains("absolute path", exception.ToString(), StringComparison.Ordinal);
    }
}
