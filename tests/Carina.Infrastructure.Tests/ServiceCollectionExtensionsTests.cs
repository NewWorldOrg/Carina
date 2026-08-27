using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Events;
using Carina.Domain.Integrity;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Scans;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.DependencyInjection;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Events;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Scanning;
using Carina.Infrastructure.Thumbnails;

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
    public void RegistersEverythingARecordingTickReachesFor()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<RecordingRepository>(
            scope.ServiceProvider.GetRequiredService<IRecordingRepository>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<RecordingRound>());
        Assert.NotNull(provider.GetRequiredService<RecordingSettings>());
    }

    [Fact]
    public void TheReservationLedgerIsReachableThroughItsRepository()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<ReservationRepository>(
            scope.ServiceProvider.GetRequiredService<IReservationRepository>());
    }

    [Fact]
    public void RegistersEverythingTheOneAllocationEntryPointReachesFor()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ReservationSchedulingService>());
        Assert.Same(RollingHorizon.Default, provider.GetRequiredService<RollingHorizon>());
    }

    [Fact]
    public void TheRecordingTickIsOneOfTheJobsTheHostStarts()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Single(provider.GetServices<IHostedService>().OfType<RecordingTickJob>());
    }

    [Fact]
    public void TheHeadTheRecorderRunsWithIsReadFromConfiguration()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["Recording:TuningLead"] = "00:00:40";

        using ServiceProvider provider = Build(settings);

        Assert.Equal(TimeSpan.FromSeconds(40), provider.GetRequiredService<RecordingSettings>().TuningLead);
    }

    [Fact]
    public void RegistersEverythingACollectionSweepReachesFor()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<BroadcastStreamDirectory>(
            scope.ServiceProvider.GetRequiredService<IBroadcastStreamDirectory>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<CollectionRound>());
    }

    [Fact]
    public void TheCollectorRunsAlongsideTheOtherHostedServices()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Single(provider.GetServices<IHostedService>().OfType<EpgCollector>());
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
    public void RegistersTheLedgerCheckAlongsideTheOtherHostedServices()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => ReferenceEquals(service, provider.GetRequiredService<IntegrityCheckJob>()));
    }

    [Fact]
    public void RegistersEverythingTheLedgerCheckReachesFor()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<LocalRecordingFileSurvey>(provider.GetRequiredService<IRecordingFileSurvey>());
        Assert.IsType<IntegrityCheckRepository>(
            scope.ServiceProvider.GetRequiredService<IIntegrityCheckRepository>());
        Assert.IsType<RecordingLedger>(scope.ServiceProvider.GetRequiredService<IRecordingLedger>());
    }

    [Fact]
    public void ReadsWhereTheOutputRootsAreMountedIntoThisProcess()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["Integrity:OutputRoots"] = "primary=/srv/recordings";
        using ServiceProvider provider = Build(settings);

        IntegritySettings read = provider.GetRequiredService<IntegritySettings>();

        Assert.Equal("primary", Assert.Single(read.OutputRoots).Root.Value);
        Assert.Equal("/srv/recordings", read.OutputRoots[0].Path);
    }

    [Fact]
    public void RejectsAnOutputRootMountedNowhere()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["Integrity:OutputRoots"] = "primary";
        using ServiceProvider provider = Build(settings);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<IntegrityOptions>>().Value);

        Assert.Contains("Integrity:OutputRoots", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RegistersTheThumbnailPassAlongsideTheOtherHostedServices()
    {
        using ServiceProvider provider = Build(ValidSettings());

        Assert.Contains(
            provider.GetServices<IHostedService>(),
            service => ReferenceEquals(service, provider.GetRequiredService<ThumbnailJob>()));
    }

    [Fact]
    public void RegistersEverythingAThumbnailPassReachesFor()
    {
        using ServiceProvider provider = Build(ValidSettings());
        using IServiceScope scope = provider.CreateScope();

        Assert.IsType<FfmpegThumbnailRenderer>(provider.GetRequiredService<IThumbnailRenderer>());
        Assert.IsType<ThumbnailWorklist>(scope.ServiceProvider.GetRequiredService<IThumbnailWorklist>());
        Assert.NotNull(provider.GetRequiredService<ThumbnailSettings>());
    }

    [Fact]
    public void ReadsWhereThePicturesGo()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["Thumbnails:WrittenTo"] = "/srv/thumbnails";
        settings["Thumbnails:Width"] = "1280";
        using ServiceProvider provider = Build(settings);

        ThumbnailSettings read = provider.GetRequiredService<ThumbnailSettings>();

        Assert.Equal("/srv/thumbnails", read.WrittenTo);
        Assert.Equal(1280, read.Width);
        Assert.True(read.DrawsAnything);
    }

    [Fact]
    public void RejectsAPictureDirectoryThisProcessCouldNotReach()
    {
        Dictionary<string, string?> settings = ValidSettings();
        settings["Thumbnails:WrittenTo"] = "srv/thumbnails";
        using ServiceProvider provider = Build(settings);

        OptionsValidationException exception = Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<ThumbnailOptions>>().Value);

        Assert.Contains("Thumbnails:WrittenTo", exception.Message, StringComparison.Ordinal);
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
