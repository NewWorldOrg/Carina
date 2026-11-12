using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Scanning;
using Carina.Infrastructure.Tests.Fixtures.Clocks;
using Carina.TestSupport;

using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Scanning;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ScanApplierTests(RepositoryDatabase database) : IAsyncLifetime
{
    private const int PhysicalChannel = 31;
    private const int OtherPhysicalChannel = 33;

    private static readonly DateTime At = StillClock.Now.UtcDateTime;
    private static readonly CancellationToken Cancel = CancellationToken.None;
    private static readonly int[] Services = [1, 2, 3];

    private readonly ServiceProvider container = database.Infrastructure(StandingStill);

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await container.DisposeAsync();


    [Fact]
    public async Task EveryServiceOnOneStreamIsWrittenWithItsOwnCandidate()
    {
        int network = BroadcastIds.NextNetwork();
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);
        var measured = SignalMeasurement.WithLock(At, 21_000);

        ScanApplication applied = await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, measured)))],
            []));

        Assert.Equal(3, applied.ServicesAdded);
        Assert.Equal(3, applied.ChannelsAdded);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository candidates = Candidates(reading);

        foreach (int service in Services)
        {
            CandidateChannel stored = Assert.Single(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));

            Assert.Equal(PhysicalChannel, stored.Tuning.PhysicalChannel);
            Assert.Equal(21_000, stored.LastMeasurement?.CnrMilliDecibels);
            Assert.True(stored.IsSelected);
        }
    }

    [Fact]
    public async Task RescanningTheSameStreamRenamesTheServicesAndLeavesTheirChannelsAlone()
    {
        int network = BroadcastIds.NextNetwork();
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);

        await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 21_000))))],
            []));

        ScanApplication again = await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Updated,
                network,
                service,
                $"Service {service} renamed",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 22_000))))],
            []));

        Assert.Equal(3, again.ServicesUpdated);
        Assert.Equal(0, again.ChannelsAdded);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IBroadcastServiceRepository services = BroadcastServices(reading);
        ICandidateChannelRepository candidates = Candidates(reading);

        foreach (int service in Services)
        {
            BroadcastService? stored = await services.FindAsync(new NetworkId(network), new ServiceId(service), Cancel);
            Assert.Equal($"Service {service} renamed", stored?.Name);
            Assert.Single(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));
        }
    }

    [Fact]
    public async Task AStreamThatHasGoneTakesItsServicesAndTheirChannelsWithIt()
    {
        int network = BroadcastIds.NextNetwork();
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);

        await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 21_000))))],
            []));

        ScanApplication gone = await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Missing,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Missing, carrying, null)))],
            []));

        Assert.Equal(3, gone.ServicesRemoved);
        Assert.Equal(3, gone.ChannelsRemoved);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IBroadcastServiceRepository services = BroadcastServices(reading);
        ICandidateChannelRepository candidates = Candidates(reading);

        foreach (int service in Services)
        {
            Assert.Null(await services.FindAsync(new NetworkId(network), new ServiceId(service), Cancel));
            Assert.Empty(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));
        }
    }

    [Fact]
    public async Task AnApplyThatFailsPartWayThroughLeavesNothingBehind()
    {
        int network = BroadcastIds.NextNetwork();
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);
        var difference = new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 21_000))))],
            []);

        int arrived = 0;
        var events = new RecordingAppEvents();
        await using ServiceProvider refusing = database.Infrastructure(replace =>
        {
            StandingStill(replace);
            replace.AddSingleton<IAppEventPublisher>(events);
            replace.AddScoped<ICandidateChannelRepository>(provider => new RefusingCandidates(
                new CandidateChannelRepository(provider.GetRequiredService<CarinaDbContext>()),
                () => ++arrived > 1));
        });
        await using AsyncServiceScope writing = refusing.CreateAsyncScope();
        ScanApplier applier = writing.ServiceProvider.GetRequiredService<ScanApplier>();

        await Assert.ThrowsAsync<StoreRefusedException>(
            () => applier.ApplyAsync(difference, [TuneSystem.IsdbT], Cancel));

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IBroadcastServiceRepository services = BroadcastServices(reading);
        ICandidateChannelRepository candidates = Candidates(reading);

        foreach (int service in Services)
        {
            Assert.Null(await services.FindAsync(new NetworkId(network), new ServiceId(service), Cancel));
            Assert.Empty(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));
        }

        Assert.Empty(events.Signalled);
    }

    [Fact]
    public async Task AServiceWhoseChannelsAllStayedTheSameIsStillMovedOffTheOneChosenBlind()
    {
        int network = BroadcastIds.NextNetwork();
        var networkId = new NetworkId(network);
        var serviceId = new ServiceId(Services[0]);

        await ApplyAsync(new ScanDifference(
            [
                new ScanServiceChange(
                    ScanChangeKind.Added,
                    networkId,
                    serviceId,
                    "Two ways in",
                    ServiceCategory.Television,
                    [
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(PhysicalChannel), null),
                        Channel(ScanChangeKind.Added, TuningParameters.Terrestrial(OtherPhysicalChannel), null),
                    ],
                    true),
            ],
            []));

        await using (AsyncServiceScope measuring = container.CreateAsyncScope())
        {
            ICandidateChannelRepository walked = Candidates(measuring);

            foreach (CandidateChannel candidate in await walked.ListForServiceAsync(networkId, serviceId, Cancel))
            {
                candidate.RecordTuningSuccess(
                    SignalMeasurement.WithLock(
                        At,
                        candidate.Tuning.PhysicalChannel == OtherPhysicalChannel ? 35_000 : 16_000),
                    At);

                await walked.SaveAsync(candidate, Cancel);
            }
        }

        await ApplyAsync(ScanDifference.Nothing);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        ICandidateChannelRepository candidates = Candidates(reading);
        CandidateChannel? selected = await candidates.FindSelectedAsync(networkId, serviceId, Cancel);

        Assert.Equal(OtherPhysicalChannel, selected?.Tuning.PhysicalChannel);
        Assert.Equal(35_000, selected?.SelectionMeasurement?.CnrMilliDecibels);
        Assert.Single(
            await candidates.ListForServiceAsync(networkId, serviceId, Cancel),
            candidate => candidate.IsSelected);
    }

    private async Task<ScanApplication> ApplyAsync(ScanDifference difference)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();

        return await writing.ServiceProvider.GetRequiredService<ScanApplier>()
            .ApplyAsync(difference, [TuneSystem.IsdbT], Cancel);
    }

    private static void StandingStill(IServiceCollection replace)
    {
        replace.AddSingleton<TimeProvider>(new StillClock());
        replace.AddSingleton<IAppEventPublisher>(new RecordingAppEvents());
    }

    private static ScanChannelChange Channel(
        ScanChangeKind kind,
        TuningParameters carrying,
        SignalMeasurement? measured)
        => new(kind, carrying, null, measured);

    private static ScanServiceChange Change(
        ScanChangeKind kind,
        int network,
        int service,
        string name,
        ScanChannelChange channel,
        bool seen = true)
        => new(
            kind,
            new NetworkId(network),
            new ServiceId(service),
            name,
            ServiceCategory.Television,
            [channel],
            seen);

    private static IBroadcastServiceRepository BroadcastServices(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IBroadcastServiceRepository>();

    private static ICandidateChannelRepository Candidates(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ICandidateChannelRepository>();
}
