extern alias driver;

using System.Runtime.Versioning;

using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.DependencyInjection;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using DriverConfiguration = driver::Carina.Driver.Configuration.DriverConfiguration;

namespace Carina.Api.Tests.Unit;

[SupportedOSPlatform("linux")]
public sealed class DriverSocketDefaultTests
{
    [Fact]
    public void TheAppAndTheDriverDefaultToOneSocket()
    {
        Assert.Equal(DriverConfiguration.DefaultSocketPath, DriverOptions.DefaultSocketPath);
    }

    [Fact]
    public void TheShippedSettingsLeaveTheSocketToItsDefault()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), optional: false)
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Carina"] = "Host=localhost;Database=carina;Username=carina;Password=unused",
                ["CARINA_DATA_PROTECTION_KEYS"] = "/var/lib/carina/keys",
            })
            .Build();
        using ServiceProvider provider = new ServiceCollection()
            .AddLogging()
            .AddCarinaInfrastructure(configuration)
            .BuildServiceProvider();

        DriverOptions options = provider.GetRequiredService<IOptions<DriverOptions>>().Value;

        Assert.Equal("/run/carina/driver.sock", options.SocketPath);
    }
}
