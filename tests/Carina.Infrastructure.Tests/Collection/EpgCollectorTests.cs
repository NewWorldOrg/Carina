using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Tests.Scanning;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Carina.Infrastructure.Tests.Collection;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EpgCollectorTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;


    [Fact]
    public async Task ASweepVisitsWhatTheDirectoryOffersAndWritesTheGuideItGathers()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();

        driver.Script(TuningParameters.Terrestrial(22), new ChannelScript { Bytes = Schedule(network) });

        await using ServiceProvider provider = Provider(driver, Offering(network, 22));
        EpgCollector collector = provider.GetServices<IHostedService>().OfType<EpgCollector>().Single();
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        await Until(
            async () => await ProgrammeAsync(network) is not null,
            "the sweep never wrote the guide it was offered");
        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);
    }

    [Fact]
    public async Task ASweepThatIsOfferedNothingDoesNotReachForATuner()
    {
        var driver = new ScriptedDriverClient();

        await using ServiceProvider provider = Provider(driver, new OfferedStreams([]));
        await RunOneSweepAsync(provider);

        Assert.Empty(driver.Started);
    }

    [Fact]
    public async Task ADriverThatRestartsMidSweepLeavesTheStreamMarkedInterrupted()
    {
        int network = NextNetwork();
        var driver = new ScriptedDriverClient();
        var relay = new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance);

        driver.Script(
            TuningParameters.Terrestrial(22),
            new ChannelScript { Paced = () => PacedStream.InChunksOf(Schedule(network), 188) });

        await using ServiceProvider provider = Provider(driver, Offering(network, 22), relay);
        EpgCollector collector = provider.GetServices<IHostedService>().OfType<EpgCollector>().Single();
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        await Until(() => driver.Started.Count > 0, "the sweep never reached for a tuner");

        relay.Publish(DriverClientSignals.InstanceChanged);

        StreamVisit? recorded = null;

        await Until(
            async () => (recorded = await RecordedAsync(network)) is not null,
            "the interrupted visit was never written down");

        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);

        Assert.Equal(VisitOutcome.Interrupted, recorded!.Outcome);
        Assert.Equal(0, recorded.ConsecutiveIncomplete);
    }

    private async Task<StreamVisit?> RecordedAsync(int network)
    {
        await using CarinaDbContext reading = database.Open();

        return (await new StreamVisitRepository(reading).ListAsync(Cancel))
            .FirstOrDefault(visit => visit.NetworkId.Value == network);
    }

    private static Task Until(Func<bool> settled, string complaint)
        => Until(() => Task.FromResult(settled()), complaint);

    private static async Task Until(Func<Task<bool>> settled, string complaint)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            if (await settled())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(25), Cancel);
        }

        Assert.Fail(complaint);
    }

    private static async Task RunOneSweepAsync(ServiceProvider provider)
    {
        EpgCollector collector = provider.GetServices<IHostedService>().OfType<EpgCollector>().Single();
        using var stopping = new CancellationTokenSource();

        await collector.StartAsync(stopping.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), Cancel);
        await stopping.CancelAsync();
        await collector.StopAsync(Cancel);
    }

    private async Task<Programme?> ProgrammeAsync(int network)
    {
        await using CarinaDbContext reading = database.Open();

        return await new ProgrammeRepository(reading).FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(1)),
            Cancel);
    }

    private ServiceProvider Provider(
        ScriptedDriverClient driver,
        OfferedStreams offered,
        DriverSignalRelay? relay = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScoped(_ => database.Open());
        services.AddScoped<IProgrammeRepository>(scope =>
            new ProgrammeRepository(scope.GetRequiredService<CarinaDbContext>()));
        services.AddScoped<IStreamVisitRepository>(scope =>
            new StreamVisitRepository(scope.GetRequiredService<CarinaDbContext>()));
        services.AddScoped<IBroadcastStreamDirectory>(_ => offered);
        services.AddSingleton<IDriverClient>(driver);
        services.AddSingleton(new CollectionSettings());
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddSingleton(provider => new RescanNoticeBoard(
            new SilentEvents(),
            provider.GetRequiredService<TimeProvider>()));
        services.AddSingleton<IDriverSignals>(relay ?? new DriverSignalRelay(NullLogger<DriverSignalRelay>.Instance));
        services.AddScoped(scope => new ProgrammeWriter(
            scope.GetRequiredService<IProgrammeRepository>(),
            new UnguardedWrites(),
            new StillClock()));
        services.AddScoped(scope => new StreamVisitor(
            scope.GetRequiredService<IDriverClient>(),
            scope.GetRequiredService<ProgrammeWriter>(),
            scope.GetRequiredService<CollectionSettings>()));
        services.AddScoped(scope => new CollectionRound(
            scope.GetRequiredService<IStreamVisitRepository>(),
            scope.GetRequiredService<IProgrammeRepository>(),
            scope.GetRequiredService<StreamVisitor>(),
            scope.GetRequiredService<RescanNoticeBoard>(),
            scope.GetRequiredService<CollectionSettings>(),
            scope.GetRequiredService<TimeProvider>(),
            NullLogger<CollectionRound>.Instance));
        services.AddHostedService<EpgCollector>();

        return services.BuildServiceProvider();
    }

    private static OfferedStreams Offering(int network, int channel)
        => new([
            new BroadcastStream(
                new NetworkId(network),
                new TransportStreamId(1),
                TuningParameters.Terrestrial(channel),
                [new ServiceId(1049)]),
        ]);

    private static int NextNetwork() => BroadcastIds.NextNetwork();

    private static byte[] Schedule(int network)
        => [.. new TransportStreamWriter(EventInformationTable.Pid)
            .Sections(new SectionWriter
            {
                TableId = EventInformationTable.FirstScheduleActualTableId,
                TableIdExtension = 1049,
                LastSectionNumber = 0,
                Body =
                [
                    0x00, 0x01,
                    (byte)(network >> 8), (byte)(network & 0xFF),
                    0x00, EventInformationTable.FirstScheduleActualTableId,
                    0x00, 0x01,
                    0xEF, 0x55, 0x22, 0x57, 0x00,
                    0x00, 0x03, 0x00,
                    0x00, 0x00,
                ],
            }.ToBytes())
            .Packets
            .SelectMany(packet => packet.ToArray())];
}

internal sealed class OfferedStreams(IReadOnlyList<BroadcastStream> streams) : IBroadcastStreamDirectory
{
    public Task<IReadOnlyList<BroadcastStream>> ListAsync(CancellationToken cancellationToken)
        => Task.FromResult(streams);
}
