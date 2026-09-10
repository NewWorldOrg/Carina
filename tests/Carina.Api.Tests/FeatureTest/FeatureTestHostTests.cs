using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

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
