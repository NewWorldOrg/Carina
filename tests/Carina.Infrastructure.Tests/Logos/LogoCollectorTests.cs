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
    private const int SomePictureType = 0x05;
    private const int AnotherPictureType = 0x03;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly DateTime At = new(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);

    private static readonly int[] PhysicalChannels = [27, 28, 29];

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
    public async Task OneWakeWorksThroughEveryTransportThatIsDueRatherThanStoppingAfterTheFirst()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        ScriptedDriverClient driver = Airing(network, transports: 3);

        await using ServiceProvider provider = Provider(network, driver, transports: 3);
        await RunAsync(provider, async () => (await VisitsAsync(network)).Count is 3);

        Assert.Equal(
            [Tuned(27), Tuned(28), Tuned(29)],
            driver.Started);
    }

    [Fact]
    public async Task AWakeStopsOnceItsBudgetIsSpentAndLeavesTheRestDueForTheNextOne()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        byte[] airing = OnTheAir(network);
        PacedStream held = PacedStream.InChunksOf(airing, airing.Length);
        HandTurnedClock clock = new(At);
        ScriptedDriverClient driver = Airing(network, transports: 2);

        driver.Script(Tuned(27), new ChannelScript { Paced = () => held });

        await using ServiceProvider provider = Provider(network, driver, transports: 2, clock: clock);
        LogoCollector collector = CollectorIn(provider);
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        held.AwaitParkedBefore(1);
        clock.Turn(TimeSpan.FromMinutes(45));
        held.Allow(2);
        await SettledAsync(async () => (await VisitsAsync(network)).Count > 0);
        await Task.Delay(TimeSpan.FromMilliseconds(400), Cancel);
        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);

        Assert.Equal([Tuned(27)], driver.Started);
        LogoVisit only = Assert.Single(await VisitsAsync(network));
        Assert.Equal(new TransportStreamId(SomeTransportStreamId), only.TransportStreamId);
    }

    [Fact]
    public async Task AWakeThatFindsEveryTransportCollectedRecentlyAsksForNoTunerAtAll()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        await CollectedAsync(network, SomeTransportStreamId, At);
        await CollectedAsync(network, SomeTransportStreamId + 1, At);
        ScriptedDriverClient driver = Airing(network, transports: 2);

        await using ServiceProvider provider = Provider(
            network,
            driver,
            transports: 2,
            clock: new HandTurnedClock(At));
        await SettleAsync(provider);

        Assert.Empty(driver.Purposes);
        Assert.Equal(2, (await VisitsAsync(network)).Count);
    }

    [Fact]
    public async Task AVisitTakenOffTheTunerKeepsWhatItGatheredAndStaysDueForTheNextWake()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        byte[] airing = OnTheAir(network, AnotherPictureType);
        PacedStream held = PacedStream.InChunksOf(airing, airing.Length);
        var signals = new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance);
        ScriptedDriverClient driver = Airing(network);

        driver.Script(Tuned(27), new ChannelScript { Paced = () => held });

        await using ServiceProvider provider = Provider(network, driver, signals: signals);
        LogoCollector collector = CollectorIn(provider);
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        held.AwaitParkedBefore(1);
        held.Allow(1);
        held.AwaitParkedBefore(2);
        signals.Publish(DriverClientSignals.InstanceChanged);
        await SettledAsync(async () => (await VisitsAsync(network)).Count > 0);
        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);

        LogoVisit cut = Assert.Single(await VisitsAsync(network));
        Assert.Equal(LogoVisitOutcome.Interrupted, cut.Outcome);
        Assert.NotNull(await LogoAsync(network));
        Assert.Equal(cut.LastAttemptedAt, cut.DueAt(new LogoSweepSettings()));
    }

    [Fact]
    public async Task ATunerNobodyCanSpareLeavesNoVisitBehindSoTheSweepTriesAgainRatherThanWaitingOut()
    {
        int network = BroadcastIds.NextNetwork();
        await SeedAsync(network, SomeServiceId, SilentServiceId);
        ScriptedDriverClient driver = Airing(network, transports: 2);
        driver.BusyRefusalsRemaining = 1000;

        await using ServiceProvider provider = Provider(network, driver, transports: 2);
        await SettleAsync(provider);

        Assert.Equal([SessionPurpose.Logo], driver.Purposes);
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

    private static TuningParameters Tuned(int physicalChannel) => TuningParameters.Terrestrial(physicalChannel);

    private static LogoCollector CollectorIn(ServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<LogoCollector>().Single();

    private static async Task RunAsync(ServiceProvider provider, Func<Task<bool>> settled)
    {
        LogoCollector collector = CollectorIn(provider);
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        await SettledAsync(settled);
        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);

        Assert.True(await settled(), "the logo sweep never wrote down what it collected");
    }

    private static async Task SettledAsync(Func<Task<bool>> settled)
    {
        for (int attempt = 0; attempt < 200 && !await settled(); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), Cancel);
        }
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

    private async Task CollectedAsync(int network, int transportStreamId, DateTime at)
    {
        await using CarinaDbContext context = database.Open();

        await new LogoVisitRepository(context).RecordAsync(
            new NetworkId(network),
            new TransportStreamId(transportStreamId),
            LogoVisitOutcome.Collected,
            at,
            Cancel);
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
        int transports = 1,
        LogoSweepSettings? settings = null,
        TimeProvider? clock = null,
        IDriverSignals? signals = null)
    {
        var services = new ServiceCollection();
        var offered = new OfferedTransports(Streams(network, transports));

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
        services.AddScoped<LogoRound>();
        services.AddSingleton<IDriverClient>(driver);
        services.AddSingleton(signals ?? new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance));
        services.AddSingleton(settings ?? new LogoSweepSettings());
        services.AddSingleton(clock ?? TimeProvider.System);
        services.AddHostedService<LogoCollector>();

        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<BroadcastStream> Streams(int network, int transports)
        => [.. Enumerable.Range(0, transports).Select(at => new BroadcastStream(
            new NetworkId(network),
            new TransportStreamId(SomeTransportStreamId + at),
            Tuned(PhysicalChannels[at]),
            [new ServiceId(SomeServiceId), new ServiceId(SilentServiceId)]))];

    private static ScriptedDriverClient Airing(int network, int transports = 1)
    {
        var driver = new ScriptedDriverClient();

        foreach (int physicalChannel in PhysicalChannels.Take(transports))
        {
            driver.Script(Tuned(physicalChannel), new ChannelScript { Bytes = OnTheAir(network) });
        }

        return driver;
    }

    private static byte[] OnTheAir(int network, int pictureType = SomePictureType)
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
                        pictureType,
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
