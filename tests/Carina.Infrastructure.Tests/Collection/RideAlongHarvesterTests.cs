using Carina.Broadcast.Tables;
using Carina.BroadcastTestSupport;
using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.Programmes;
using Carina.Infrastructure.Collection;
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
public sealed class RideAlongHarvesterTests(RepositoryDatabase database)
{
    private static readonly CancellationToken Cancel = CancellationToken.None;


    [Fact]
    public async Task ARecordingTakesTheGuideAlongWithItWithoutAskingForATuner()
    {
        int network = NextNetwork();
        ScriptedDriverClient driver = Recording(network, out SessionId recording);

        await using ServiceProvider provider = Provider(driver);
        await RunAsync(provider, async () => await ProgrammeAsync(network) is not null);

        Assert.Empty(driver.Started);
        Assert.Equal([DriverEndpoints.PiggybackSubscriber], driver.RiddenAs);
        Assert.NotEqual(default, recording);
    }

    [Fact]
    public async Task OurOwnCollectionSessionsAreNotRiddenAlongWith()
    {
        int network = NextNetwork();
        ScriptedDriverClient driver = Recording(network, out _, SessionPurpose.Survey);

        await using ServiceProvider provider = Provider(driver);
        RideAlongHarvester harvester = HarvesterIn(provider);
        using var stopping = new CancellationTokenSource();

        await harvester.StartAsync(stopping.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Cancel);
        await stopping.CancelAsync();
        await harvester.StopAsync(Cancel);

        Assert.Empty(driver.RiddenAs);
    }

    [Fact]
    public async Task RidingAlongCanBeTurnedOff()
    {
        int network = NextNetwork();
        ScriptedDriverClient driver = Recording(network, out _);

        await using ServiceProvider provider = Provider(driver, new CollectionSettings { RidesAlong = false });
        RideAlongHarvester harvester = HarvesterIn(provider);
        using var stopping = new CancellationTokenSource();

        await harvester.StartAsync(stopping.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(300), Cancel);
        await stopping.CancelAsync();
        await harvester.StopAsync(Cancel);

        Assert.Empty(driver.RiddenAs);
    }

    [Fact]
    public async Task OneSessionIsOnlyRiddenOnceHoweverOftenItIsSeen()
    {
        int network = NextNetwork();
        ScriptedDriverClient driver = Recording(network, out _);

        await using ServiceProvider provider = Provider(
            driver,
            new CollectionSettings { BetweenSessionChecks = TimeSpan.FromMilliseconds(20) });
        RideAlongHarvester harvester = HarvesterIn(provider);
        using var stopping = new CancellationTokenSource();

        await harvester.StartAsync(stopping.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(400), Cancel);
        await stopping.CancelAsync();
        await harvester.StopAsync(Cancel);

        Assert.Single(driver.RiddenAs);
    }

    private static RideAlongHarvester HarvesterIn(ServiceProvider provider)
        => provider.GetServices<IHostedService>().OfType<RideAlongHarvester>().Single();

    private static async Task RunAsync(ServiceProvider provider, Func<Task<bool>> settled)
    {
        RideAlongHarvester harvester = HarvesterIn(provider);
        using var stopping = new CancellationTokenSource();

        await harvester.StartAsync(stopping.Token);

        for (int attempt = 0; attempt < 200 && !await settled(); attempt++)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(25), Cancel);
        }

        await stopping.CancelAsync();
        await harvester.StopAsync(Cancel);

        Assert.True(await settled(), "riding along never wrote the guide it gathered");
    }

    private static ScriptedDriverClient Recording(
        int network,
        out SessionId sessionId,
        SessionPurpose purpose = SessionPurpose.Recording)
    {
        var driver = new ScriptedDriverClient();
        TuningParameters tuning = TuningParameters.Terrestrial(22);

        driver.Script(tuning, new ChannelScript { Bytes = Schedule(network) });
        sessionId = SessionId.Parse("recording-1");
        driver.Hold(sessionId, tuning);
        driver.Open.Add(new SessionSnapshot(
            sessionId,
            purpose,
            driver.DeviceId,
            SessionState.Active,
            DateTimeOffset.UnixEpoch));

        return driver;
    }

    private ServiceProvider Provider(ScriptedDriverClient driver, CollectionSettings? settings = null)
    {
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddScoped(_ => database.Open());
        services.AddScoped<IProgrammeRepository>(scope =>
            new ProgrammeRepository(scope.GetRequiredService<CarinaDbContext>()));
        services.AddScoped(scope => new ProgrammeWriter(
            scope.GetRequiredService<IProgrammeRepository>(),
            new UnguardedWrites(),
            new StillClock(),
            new SilentEvents()));
        services.AddSingleton<IDriverClient>(driver);
        services.AddSingleton(settings ?? new CollectionSettings
        {
            BetweenSessionChecks = TimeSpan.FromMilliseconds(20),
        });
        services.AddSingleton<TimeProvider>(TimeProvider.System);
        services.AddHostedService<RideAlongHarvester>();

        return services.BuildServiceProvider();
    }

    private async Task<Programme?> ProgrammeAsync(int network)
    {
        await using CarinaDbContext reading = database.Open();

        return await new ProgrammeRepository(reading).FindAsync(
            new ProgrammeId(new NetworkId(network), new ServiceId(1049), new EventId(1)),
            Cancel);
    }

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
