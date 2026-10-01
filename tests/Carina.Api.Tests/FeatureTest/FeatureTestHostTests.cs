using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Npgsql;

namespace Carina.Api.Tests.FeatureTest;

public sealed class FeatureTestHostTests
{
    [Fact]
    public void TheHostNamesEverySettingItselfRatherThanLeavingOneToTheMachine()
    {
        string[] left = [];

        using TestingWebApplicationFactory factory = new();
        using WebApplicationFactory<Program> wired = factory.WithWebHostBuilder(builder =>
            left = [.. TestingWebApplicationFactory.SettingsNamedHere
                .Where(setting => builder.GetSetting(setting) is null)]);

        wired.CreateClient().Dispose();

        Assert.Empty(left);
    }

    [Fact(DisplayName = "an application raised from a host reshaped more than once is stopped when the host it came from is let go of")]
    public async Task AnApplicationRaisedFromAHostReshapedMoreThanOnceIsStoppedWithIt()
    {
        TestingWebApplicationFactory factory = new();
        WebApplicationFactory<Program> reshapedTwice = factory
            .WithWebHostBuilder(_ => { })
            .WithWebHostBuilder(_ => { });

        reshapedTwice.CreateClient().Dispose();

        IHostApplicationLifetime raised = reshapedTwice.Services.GetRequiredService<IHostApplicationLifetime>();

        await factory.DisposeAsync();

        Assert.True(
            raised.ApplicationStopped.IsCancellationRequested,
            "the application is still running after the host it was raised from was let go of, "
            + "and its entry point and everything it started stay behind in this process");
    }

    [Fact(DisplayName = "letting go of the host without waiting stops the applications raised from it as well")]
    public void LettingGoWithoutWaitingStopsTheApplicationsRaisedFromIt()
    {
        TestingWebApplicationFactory factory = new();
        WebApplicationFactory<Program> reshapedTwice = factory
            .WithWebHostBuilder(_ => { })
            .WithWebHostBuilder(_ => { });

        reshapedTwice.CreateClient().Dispose();

        IHostApplicationLifetime raised = reshapedTwice.Services.GetRequiredService<IHostApplicationLifetime>();

        factory.Dispose();

        Assert.True(raised.ApplicationStopped.IsCancellationRequested);
    }

    [Fact]
    public void TheDatabaseThisHostNamesIsOneNoResolverIsAskedAbout()
    {
        string? named = new NpgsqlConnectionStringBuilder(
            TestingWebApplicationFactory.DatabaseNoResolverIsAskedAbout).Host;

        Assert.True(
            Path.IsPathRooted(named),
            $"the database is named '{named}', so every surface that reads one waits for a resolver "
            + "to say there is no such host. How long that takes is the machine's, not this suite's: "
            + "name it by a path, which the kernel refuses at once and alike everywhere");
        Assert.False(
            Directory.Exists(named),
            $"'{named}' is there on this machine, so these tests would stop being about an "
            + "application whose database is absent");
    }
}
