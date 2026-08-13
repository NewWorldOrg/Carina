using Carina.Driver.Configuration;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Tests;

public sealed class DriverHostTests
{
    private static readonly DriverConfiguration Configuration =
        new(
            "/run/carina/driver.sock",
            "/srv/recordings",
            6,
            new TunerSettings(TunerBackend.Fake),
            [new DeviceSettings("adapter0", DeviceKind.Terrestrial)]
        );

    [Fact]
    public void BuildsTheHost()
    {
        using var host = DriverHost.Create([], Configuration);

        Assert.NotNull(host.Services.GetService(typeof(IHostApplicationLifetime)));
    }

    [Fact]
    public void TheConfigurationIsAvailableToTheServices()
    {
        using var host = DriverHost.Create([], Configuration);

        Assert.Same(
            Configuration,
            host.Services.GetService(typeof(DriverConfiguration))
        );
    }
}
