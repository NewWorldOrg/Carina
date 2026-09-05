using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Logos;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Logos;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LogoCollectorTests(RepositoryDatabase database)
{
    private const int SomeServiceId = 1024;
    private const int SilentServiceId = 1025;
    private const int SomeLogoId = 261;
    private const int SomeTransportStreamId = 32737;
    private const int AnotherTransportStreamId = 32738;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ALogoOnTheAirEndsUpKeptAndNamedByTheServiceThatUsesIt()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);

        await using ServiceProvider provider = Provider(network, Airing(network));
        await RunAsync(provider, async () => (await VisitsAsync(network)).Count > 0);

        StationLogo? kept = await LogoAsync(network);
        Assert.Equal(64, kept!.Width);
        Assert.Equal(36, kept.Height);
        Assert.Equal(new LogoId(SomeLogoId), (await ServiceAsync(network, SomeServiceId))!.LogoId);
    }

    [Fact]
    public async Task AStationThatBroadcastsNoPictureIsWrittenDownAsHavingNoneRatherThanLeftUnknown()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);

        await using ServiceProvider provider = Provider(network, Airing(network));
        await RunAsync(provider, async () => (await VisitsAsync(network)).Count > 0);

        BroadcastService? silent = await ServiceAsync(network, SilentServiceId);
        Assert.Null(silent!.LogoId);
        Assert.Equal(StationLogoDeclaration.NoPictureIsBroadcast, silent.LogoDeclaration);
    }

    [Fact]
    public async Task TheSweepAsksForTheTunerOnTheRungEverythingElseOutranks()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        ScriptedDriverClient driver = Airing(network);

        await using ServiceProvider provider = Provider(network, driver);
        await RunAsync(provider, async () => (await VisitsAsync(network)).Count > 0);

        Assert.Equal([SessionPurpose.Logo], driver.Purposes.Distinct());
    }

    [Fact]
    public async Task OneSweepOpensOneTransportSoTheTunerGoesBackBetweenThem()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        ScriptedDriverClient driver = Airing(network, alsoOnAnotherTransport: true);

        await using ServiceProvider provider = Provider(network, driver, alsoOnAnotherTransport: true);
        await RunAsync(provider, async () => (await VisitsAsync(network)).Count > 0);

        Assert.Single(driver.Started);
    }

    [Fact]
    public async Task ATunerNobodyCanSpareLeavesNoVisitBehindSoTheSweepTriesAgainRatherThanWaitingOut()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        ScriptedDriverClient driver = Airing(network);
        driver.BusyRefusalsRemaining = 1000;

        await using ServiceProvider provider = Provider(network, driver);
        await SettleAsync(provider);

        Assert.Equal([SessionPurpose.Logo], driver.Purposes.Distinct());
        Assert.Empty(driver.Started);
        Assert.Empty(await VisitsAsync(network));
        Assert.Null(await LogoAsync(network));
    }

    [Fact]
    public async Task CollectingLogosCanBeTurnedOffAndThenNoTunerIsAskedForAtAll()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        ScriptedDriverClient driver = Airing(network);

        await using ServiceProvider provider = Provider(
            network,
            driver,
            settings: new LogoSweepSettings { Collects = false });
        await SettleAsync(provider);

        Assert.Empty(driver.Started);
    }

    [Fact]
    public async Task AVisitThatCollectedSomethingIsWrittenDownSoTheNextSweepGoesElsewhere()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);

        await using ServiceProvider provider = Provider(network, Airing(network));
        await RunAsync(provider, async () => (await VisitsAsync(network)).Count > 0);

        LogoVisit visit = Assert.Single(await VisitsAsync(network));
        Assert.Equal(LogoVisitOutcome.Collected, visit.Outcome);
        Assert.NotNull(visit.LastCollectedAt);
    }

    private static LogoCollector CollectorIn(ServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<LogoCollector>().Single();

    private static async Task RunAsync(ServiceProvider provider, Func<Task<bool>> settled)
    {
        LogoCollector collector = CollectorIn(provider);
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);

        for (int attempt = 0; attempt < 200 && !await settled(); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), Cancel);
        }

        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);

        Assert.True(await settled(), "the logo sweep never wrote down what it collected");
    }

    private static async Task SettleAsync(ServiceProvider provider)
    {
        LogoCollector collector = CollectorIn(provider);
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(400), Cancel);
        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);
    }

    private async Task SeedAsync(int network, params int[] services)
    {
        await using CarinaDbContext context = database.Open();
        var repository = new BroadcastServiceRepository(context);

        foreach (int service in services)
        {
            await repository.AddAsync(
                BroadcastService.Discover(
                    new NetworkId(network),
                    new ServiceId(service),
                    "Fixture Service",
                    ServiceCategory.Television,
                    At),
                Cancel);
        }
    }

    private async Task<StationLogo?> LogoAsync(int network)
    {
        await using CarinaDbContext reading = database.Open();

        return await new StationLogoRepository(reading)
            .FindAsync(new NetworkId(network), new LogoId(SomeLogoId), Cancel);
    }

    private async Task<BroadcastService?> ServiceAsync(int network, int service)
    {
        await using CarinaDbContext reading = database.Open();

        return await new BroadcastServiceRepository(reading)
            .FindAsync(new NetworkId(network), new ServiceId(service), Cancel);
    }

    private async Task<IReadOnlyList<LogoVisit>> VisitsAsync(int network)
    {
        await using CarinaDbContext reading = database.Open();
        IReadOnlyList<LogoVisit> visits = await new LogoVisitRepository(reading).ListAsync(Cancel);

        return [.. visits.Where(visit => visit.NetworkId.Value == network)];
    }

    private ServiceProvider Provider(
        int network,
        ScriptedDriverClient driver,
        bool alsoOnAnotherTransport = false,
        LogoSweepSettings? settings = null)
    {
        var services = new ServiceCollection();
        var offered = new OfferedTransports(Streams(network, alsoOnAnotherTransport));

        services.AddLogging();
        services.AddScoped(_ => database.Open());
        services.AddScoped<IStationLogoRepository>(scope =>
            new StationLogoRepository(scope.GetRequiredService<CarinaDbContext>()));
        services.AddScoped<IBroadcastServiceRepository>(scope =>
            new BroadcastServiceRepository(scope.GetRequiredService<CarinaDbContext>()));
        services.AddScoped<ILogoVisitRepository>(scope =>
            new LogoVisitRepository(scope.GetRequiredService<CarinaDbContext>()));
        services.AddScoped<IBroadcastStreamDirectory>(_ => offered);
        services.AddScoped(scope => new LogoVisitor(
            driver,
            scope.GetRequiredService<LogoSweepSettings>(),
            TimeProvider.System));
        services.AddScoped(scope => new LogoWriter(
            scope.GetRequiredService<IStationLogoRepository>(),
            scope.GetRequiredService<IBroadcastServiceRepository>(),
            TimeProvider.System));
        services.AddSingleton<IDriverClient>(driver);
        services.AddSingleton<IDriverSignals>(new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance));
        services.AddSingleton(settings ?? new LogoSweepSettings
        {
            BetweenSweeps = TimeSpan.FromMinutes(10),
        });
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddHostedService<LogoCollector>();

        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<BroadcastStream> Streams(int network, bool alsoOnAnotherTransport)
    {
        var streams = new List<BroadcastStream>
        {
            new(
                new NetworkId(network),
                new TransportStreamId(SomeTransportStreamId),
                TuningParameters.Terrestrial(27),
                [new ServiceId(SomeServiceId), new ServiceId(SilentServiceId)]),
        };

        if (alsoOnAnotherTransport)
        {
            streams.Add(new BroadcastStream(
                new NetworkId(network),
                new TransportStreamId(AnotherTransportStreamId),
                TuningParameters.Terrestrial(28),
                [new ServiceId(SomeServiceId)]));
        }

        return streams;
    }

    private static ScriptedDriverClient Airing(int network, bool alsoOnAnotherTransport = false)
    {
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(27), new ChannelScript { Bytes = OnTheAir(network) });

        if (alsoOnAnotherTransport)
        {
            driver.Script(TuningParameters.Terrestrial(28), new ChannelScript { Bytes = OnTheAir(network) });
        }

        return driver;
    }

    private static byte[] OnTheAir(int network)
    {
        var stream = new List<byte>();

        stream.AddRange(new TransportStreamWriter(CommonDataTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = CommonDataTable.TableId,
                TableIdExtension = 1,
                Body = new CdtWriter
                {
                    OriginalNetworkId = network,
                    DataModule = CdtWriter.LogoModule(
                        0x05,
                        SomeLogoId,
                        3,
                        new LogoPngWriter { Width = 64, Height = 36 }.ToBytes()),
                }.ToBody(),
            }.ToBytes())
            .Bytes);

        stream.AddRange(new TransportStreamWriter(ServiceDescriptionTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = ServiceDescriptionTable.ActualStreamTableId,
                TableIdExtension = SomeTransportStreamId,
                Body = new SdtWriter
                {
                    OriginalNetworkId = network,
                    Services =
                    [
                        SdtWriter.Service(SomeServiceId, SiDescriptorWriter.LogoNamedOnly(SomeLogoId)),
                        SdtWriter.Service(SilentServiceId, SiDescriptorWriter.LogoAsACharacterString([])),
                    ],
                }.ToBody(),
            }.ToBytes())
            .Bytes);

        return stream.ToArray();
    }

    private sealed class OfferedTransports(IReadOnlyList<BroadcastStream> streams) : IBroadcastStreamDirectory
    {
        public Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken)
            => Task.FromResult(streams);

        public Task<IReadOnlyList<IntendedStream>> ListIntendedAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
