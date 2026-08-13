using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Api.Tests.FeatureTest;

[Collection(FeatureTestCollection.Name)]
public sealed class StartupValidationTests
{
    [Fact]
    public void StartupFailsWhenTheConnectionStringIsMissing()
    {
        using var factory = new TestingWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("ConnectionStrings:Carina", ""));

        var validation = Innermost(Record.Exception(() => factory.CreateClient()));

        var failure = Assert.IsType<OptionsValidationException>(validation);
        Assert.Contains("ConnectionStrings:Carina", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailsWhenTheDriverSocketPathIsMissing()
    {
        using var factory = new TestingWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("CARINA_DRIVER_SOCKET", ""));

        var validation = Innermost(Record.Exception(() => factory.CreateClient()));

        var failure = Assert.IsType<OptionsValidationException>(validation);
        Assert.Contains("CARINA_DRIVER_SOCKET", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailsWhenTheDriverSocketPathIsRelative()
    {
        using var factory = new TestingWebApplicationFactory()
            .WithWebHostBuilder(builder => builder.UseSetting("CARINA_DRIVER_SOCKET", "driver.sock"));

        var validation = Innermost(Record.Exception(() => factory.CreateClient()));

        var failure = Assert.IsType<OptionsValidationException>(validation);
        Assert.Contains("absolute path", failure.Message, StringComparison.Ordinal);
    }

    private static Exception? Innermost(Exception? exception)
    {
        while (exception?.InnerException is { } inner)
        {
            exception = inner;
        }

        return exception;
    }
}
