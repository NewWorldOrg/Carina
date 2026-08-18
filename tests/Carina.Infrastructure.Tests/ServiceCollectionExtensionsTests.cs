using Carina.Domain.Base;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Events;
using Carina.Domain.Scans;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Events;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Scanning;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Tests;

public sealed class ServiceCollectionExtensionsTests
{
    private const string ConnectionString = "Host=db;Port=5432;Database=carina;Username=carina;Password=placeholder";

    private static ServiceProvider Build(Dictionary<string, string?> settings)
        => new ServiceCollection()
            .AddLogging()
            .AddCarinaInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(settings).Build())
            .BuildServiceProvider();

    private static Dictionary<string, string?> ValidSettings() => new()
    {
        ["ConnectionStrings:Carina"] = ConnectionString,
        ["CARINA_DRIVER_SOCKET"] = "/run/carina/driver.sock",
    };

    [Fact]
    public void RegistersThePersistenceContext()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CarinaDbContext>());
    }

    [Fact]
    public void RegistersTheScanOrchestratorAlongsideTheRepositoriesItWalksWith()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ChannelScanOrchestrator>(
            scope.ServiceProvider.GetRequiredService<IChannelScanOrchestrator>());
    }

    [Fact]
    public void RegistersTheHubItselfAsTheAppEventPublisherSoNoSignalIsDropped()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Same(
            provider.GetRequiredService<AppEventHub>(),
            provider.GetRequiredService<IAppEventPublisher>());
    }

    [Fact]
    public void RegistersSomethingToCloseTheHubWhenTheAppStops()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is AppEventHubLifetime);
    }

    [Fact]
    public void RegistersTheScanRunnerAsTheSameThingThatIsStoppedWithTheApp()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => ReferenceEquals(service, provider.GetRequiredService<ScanRunner>()));
    }

    [Fact]
    public void RegistersTheTimeProvider()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Same(TimeProvider.System, provider.GetRequiredService<TimeProvider>());
    }

    [Fact]
    public void RegistersTheMonitorBackedDriverStatusReader()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.IsType<MonitoredDriverStatusReader>(provider.GetRequiredService<IDriverStatusReader>());
    }

    [Fact]
    public void RegistersTheWriteThatLandsWholeOrNotAtAll()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<DatabaseAtomicWrite>(scope.ServiceProvider.GetRequiredService<IAtomicWrite>());
    }

    [Fact]
    public void RegistersTheDriverClientAndItsSupervision()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.IsType<DriverIpcClient>(provider.GetRequiredService<IDriverClient>());
        Assert.NotNull(provider.GetRequiredService<DriverConnectionMonitor>());
        Assert.Same(
            provider.GetRequiredService<DriverSignalRelay>(),
            provider.GetRequiredService<IDriverSignals>());
        Assert.IsType<NoopDriverSessionResyncHook>(
            provider.GetRequiredService<IDriverSessionResyncHook>());
        Assert.Same(
            DriverSupervisionSettings.Default,
            provider.GetRequiredService<DriverSupervisionSettings>());
        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => service is DriverConnectionSupervisor);
    }

    [Fact]
    public void RejectsAMissingConnectionString()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["ConnectionStrings:Carina"] = "";
        using ServiceProvider provider = Build(settings);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DatabaseOptions>>().Value);

        Assert.Contains("ConnectionStrings:Carina", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAMissingDriverSocketPath()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings.Remove("CARINA_DRIVER_SOCKET");
        using ServiceProvider provider = Build(settings);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DriverOptions>>().Value);

        Assert.Contains("CARINA_DRIVER_SOCKET", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsARelativeDriverSocketPath()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["CARINA_DRIVER_SOCKET"] = "driver.sock";
        using ServiceProvider provider = Build(settings);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<DriverOptions>>().Value);

        Assert.Contains("absolute path", exception.Message, StringComparison.Ordinal);
    }
}
