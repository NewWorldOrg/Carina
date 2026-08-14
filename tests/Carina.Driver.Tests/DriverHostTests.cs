using Carina.Contracts;
using Carina.Driver.Configuration;
using Carina.Driver.Events;
using Carina.Driver.Ipc;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Driver.Tests;

public sealed class DriverHostTests : IDisposable
{
    private readonly string root = DriverUnderTest.NewRoot();

    public DriverHostTests() => DriverUnderTest.ClearTheInheritedUrls();

    public void Dispose() => Directory.Delete(root, recursive: true);

    private DriverHostResult Build(string[]? args = null) =>
        DriverHost.Create(args ?? [], DriverUnderTest.ConfigurationIn(root));

    private static IHost HostOf(DriverHostResult result)
    {
        Assert.True(result.TryGetHost(out var host), string.Join(" ", result.Problems));

        return host;
    }

    [Fact]
    public void BuildsTheHost()
    {
        using var host = HostOf(Build());

        Assert.NotNull(host.Services.GetService(typeof(IHostApplicationLifetime)));
    }

    [Fact]
    public void TheConfigurationIsAvailableToTheServices()
    {
        var configuration = DriverUnderTest.ConfigurationIn(root);
        using var host = HostOf(DriverHost.Create([], configuration));

        Assert.Same(configuration, host.Services.GetService(typeof(DriverConfiguration)));
    }

    [Fact]
    public void TheSessionManagerIsAvailableToTheServices()
    {
        using var host = HostOf(Build());

        Assert.NotNull(host.Services.GetService(typeof(TunerSessionManager)));
        Assert.NotNull(host.Services.GetService(typeof(ITunerDeviceFactory)));
        Assert.NotNull(host.Services.GetService(typeof(TimeProvider)));
        Assert.NotNull(host.Services.GetService(typeof(DriverEventHub)));
        Assert.NotNull(host.Services.GetService(typeof(DriverHello)));
    }

    [Fact]
    public void TheSessionManagerRunsWithTheHost()
    {
        using var host = HostOf(Build());

        var hosted = (IEnumerable<IHostedService>)
            host.Services.GetService(typeof(IEnumerable<IHostedService>))!;

        Assert.Contains(hosted, service => service is TunerSessionManager);
        Assert.Contains(hosted, service => service is SocketPermissionGuard);
        Assert.Contains(hosted, service => service is DriverLifecycle);
    }

    [Fact]
    public void TheShutdownTimeoutCoversTheDrainAndTheHardStop()
    {
        using var host = HostOf(Build());

        var manager = (TunerSessionManager)host.Services.GetService(typeof(TunerSessionManager))!;
        var options = (IOptions<HostOptions>)
            host.Services.GetService(typeof(IOptions<HostOptions>))!;

        Assert.Equal(
            TimeSpan.FromHours(6) + TunerSessionManager.DefaultHardStopLimit,
            manager.ShutdownBudget
        );
        Assert.Equal(
            manager.ShutdownBudget + TimeSpan.FromMinutes(1),
            options.Value.ShutdownTimeout
        );
    }

    [Fact]
    public async Task TheSessionManagerSignalsTheEventHub()
    {
        using var host = HostOf(Build());

        var manager = (TunerSessionManager)host.Services.GetService(typeof(TunerSessionManager))!;
        var hub = (DriverEventHub)host.Services.GetService(typeof(DriverEventHub))!;

        Assert.True(hub.TryListen(out var listener));

        using (listener)
        {
            manager.Begin(DriverUnderTest.Live("hub-signal"));

            var taken = await listener.Take(
                new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token
            );

            Assert.Contains(DriverEvents.Sessions, taken);
        }
    }

    [Fact]
    public void TheGreetingCarriesTheProtocolAndTheCapabilities()
    {
        using var host = HostOf(Build());

        var hello = (DriverHello)host.Services.GetService(typeof(DriverHello))!;

        Assert.Equal(DriverProtocol.Version, hello.ProtocolVersion);
        Assert.NotNull(hello.InstanceId);
        Assert.True(hello.Supports(DriverCapabilities.Recording));
        Assert.True(hello.Supports(DriverCapabilities.Live));
        Assert.True(hello.Supports(DriverCapabilities.QualityMetering));
        Assert.True(hello.Supports(DriverCapabilities.DeviceDetection));
        Assert.True(hello.Supports(DriverCapabilities.TunerLedger));
    }

    [Fact]
    public void EachDriverAnnouncesItselfAsADifferentInstance()
    {
        using var first = HostOf(Build());
        using var second = HostOf(DriverHost.Create([], DriverUnderTest.ConfigurationIn(root)));

        var one = (DriverHello)first.Services.GetService(typeof(DriverHello))!;
        var other = (DriverHello)second.Services.GetService(typeof(DriverHello))!;

        Assert.True(one.IsDifferentInstanceFrom(other));
    }

    [Fact]
    public void AUrlOnTheCommandLineStopsTheDriver()
    {
        var refused = Build(["--urls", "http://0.0.0.0:8080"]);

        Assert.False(refused.TryGetHost(out _));
        Assert.Equal(DriverHostRefusal.Configuration, refused.Refusal);
        Assert.Contains(refused.Problems, problem => problem.Contains("--urls", StringComparison.Ordinal));
    }

    [Fact]
    public void AKestrelEndpointStopsTheDriver()
    {
        var refused = Build(["--Kestrel:Endpoints:Http:Url=http://0.0.0.0:5000"]);

        Assert.False(refused.TryGetHost(out _));
        Assert.Equal(DriverHostRefusal.Configuration, refused.Refusal);
        Assert.Contains(
            refused.Problems,
            problem => problem.Contains("Kestrel:Endpoints:Http", StringComparison.Ordinal)
        );
    }

    [Fact]
    public void ARefusedConfigurationBindsNothing()
    {
        var refused = Build(["--urls", "http://0.0.0.0:8080"]);

        Assert.False(refused.TryGetHost(out _));
        Assert.False(File.Exists(Path.Combine(root, "driver.sock")));
    }

    [Fact]
    public void SomethingThatIsNotASocketOnThePathStopsTheDriver()
    {
        var path = Path.Combine(root, "driver.sock");
        File.WriteAllText(path, "not a socket");

        var refused = Build();

        Assert.False(refused.TryGetHost(out _));
        Assert.Equal(DriverHostRefusal.Socket, refused.Refusal);
        Assert.Equal("not a socket", File.ReadAllText(path));
    }

    [Fact]
    public async Task TheDriverListensOnTheSocketAndNowhereElse()
    {
        await using var driver = await DriverUnderTest.Start();

        var addresses = driver
            .Service<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses;

        Assert.NotEmpty(addresses);
        Assert.All(
            addresses,
            address => Assert.StartsWith("http://unix:", address, StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task TheSocketCarriesTheModeAndTheGroup()
    {
        await using var driver = await DriverUnderTest.Start();

        var entry = UnixFile.Inspect(driver.SocketPath);

        Assert.Equal(UnixPathKind.Socket, entry.Kind);
        Assert.Equal("0660", UnixFile.Octal(entry.Permissions));
        Assert.Equal((uint)driver.Configuration.SocketGroupId, entry.GroupId);
    }

    [Fact]
    public async Task ASocketLeftBehindDoesNotStopTheNextDriver()
    {
        var root = DriverUnderTest.NewRoot();

        try
        {
            var configuration = DriverUnderTest.ConfigurationIn(root);
            var path = configuration.SocketPath!;

            using (var stale = new System.Net.Sockets.Socket(
                System.Net.Sockets.AddressFamily.Unix,
                System.Net.Sockets.SocketType.Stream,
                System.Net.Sockets.ProtocolType.Unspecified
            ))
            {
                stale.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(path));

                Assert.True(File.Exists(path));

                using var host = HostOf(DriverHost.Create([], configuration));

                await host.StartAsync();
                await host.StopAsync(TimeSpan.FromSeconds(10));
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task TheSocketIsGoneWhenTheDriverStops()
    {
        var driver = await DriverUnderTest.Start();
        var path = driver.SocketPath;

        Assert.True(File.Exists(path));

        await driver.DisposeAsync();

        Assert.False(File.Exists(path));
    }
}
