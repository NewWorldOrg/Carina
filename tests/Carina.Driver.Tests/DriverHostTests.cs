using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.Hosting;

namespace Carina.Driver.Tests;

public sealed class DriverHostTests
{
    private static readonly DriverConfiguration Configuration =
        new(
            "/run/carina/driver.sock",
            [new OutputRootSettings("primary", "/srv/recordings")],
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

    [Fact]
    public void TheSessionManagerIsAvailableToTheServices()
    {
        using var host = DriverHost.Create([], Configuration);

        Assert.NotNull(host.Services.GetService(typeof(TunerSessionManager)));
        Assert.NotNull(host.Services.GetService(typeof(ITunerDeviceFactory)));
        Assert.NotNull(host.Services.GetService(typeof(TimeProvider)));
    }

    [Fact]
    public void TheSessionManagerRunsWithTheHost()
    {
        using var host = DriverHost.Create([], Configuration);

        var hosted = (IEnumerable<IHostedService>)
            host.Services.GetService(typeof(IEnumerable<IHostedService>))!;

        Assert.Contains(hosted, service => service is TunerSessionManager);
    }
}
