using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Scanning;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Scanning;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ScanApplierDatabaseTests(RepositoryDatabase database)
{
    private const int PhysicalChannel = 31;

    private static readonly DateTime At = StillClock.Now.UtcDateTime;
    private static readonly CancellationToken Cancel = CancellationToken.None;
    private static readonly int[] Services = [1, 2, 3];

    private static int nextNetworkId = 60_000;

    [Fact]
    public async Task EveryServiceOnOneStreamIsWrittenWithItsOwnCandidate()
    {
        var network = Interlocked.Increment(ref nextNetworkId);
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);
        var measured = SignalMeasurement.WithLock(At, 21_000);

        var applied = await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, measured)))],
            []));

        Assert.Equal(3, applied.ServicesAdded);
        Assert.Equal(3, applied.ChannelsAdded);

        await using var reading = database.Open();
        var candidates = new CandidateChannelRepository(reading);

        foreach (var service in Services)
        {
            var stored = Assert.Single(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));

            Assert.Equal(PhysicalChannel, stored.Tuning.PhysicalChannel);
            Assert.Equal(21_000, stored.LastMeasurement?.CnrMilliDecibels);
            Assert.True(stored.IsSelected);
        }
    }

    [Fact]
    public async Task RescanningTheSameStreamRenamesTheServicesAndLeavesTheirChannelsAlone()
    {
        var network = Interlocked.Increment(ref nextNetworkId);
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);

        await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 21_000))))],
            []));

        var again = await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Updated,
                network,
                service,
                $"Service {service} renamed",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 22_000))))],
            []));

        Assert.Equal(3, again.ServicesUpdated);
        Assert.Equal(0, again.ChannelsAdded);

        await using var reading = database.Open();
        var services = new BroadcastServiceRepository(reading);
        var candidates = new CandidateChannelRepository(reading);

        foreach (var service in Services)
        {
            var stored = await services.FindAsync(new NetworkId(network), new ServiceId(service), Cancel);
            Assert.Equal($"Service {service} renamed", stored?.Name);
            Assert.Single(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));
        }
    }

    [Fact]
    public async Task AStreamThatHasGoneTakesItsServicesAndTheirChannelsWithIt()
    {
        var network = Interlocked.Increment(ref nextNetworkId);
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);

        await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 21_000))))],
            []));

        var gone = await ApplyAsync(new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Missing,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Missing, carrying, null)))],
            []));

        Assert.Equal(3, gone.ServicesRemoved);
        Assert.Equal(3, gone.ChannelsRemoved);

        await using var reading = database.Open();
        var services = new BroadcastServiceRepository(reading);
        var candidates = new CandidateChannelRepository(reading);

        foreach (var service in Services)
        {
            Assert.Null(await services.FindAsync(new NetworkId(network), new ServiceId(service), Cancel));
            Assert.Empty(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));
        }
    }

    [Fact]
    public async Task AnApplyThatFailsPartWayThroughLeavesNothingBehind()
    {
        var network = Interlocked.Increment(ref nextNetworkId);
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);
        var difference = new ScanDifference(
            [.. Services.Select(service => Change(
                ScanChangeKind.Added,
                network,
                service,
                $"Service {service}",
                Channel(ScanChangeKind.Added, carrying, SignalMeasurement.WithLock(At, 21_000))))],
            []);

        await using var writing = database.Open();
        var arrived = 0;
        var events = new RecordingAppEvents();
        var applier = new ScanApplier(
            new BroadcastServiceRepository(writing),
            new RefusingCandidates(new CandidateChannelRepository(writing), () => ++arrived > 1),
            new DatabaseAtomicWrite(writing),
            events,
            new StillClock());

        await Assert.ThrowsAsync<StoreRefusedException>(
            () => applier.ApplyAsync(difference, [TuneSystem.IsdbT], Cancel));

        await using var reading = database.Open();
        var services = new BroadcastServiceRepository(reading);
        var candidates = new CandidateChannelRepository(reading);

        foreach (var service in Services)
        {
            Assert.Null(await services.FindAsync(new NetworkId(network), new ServiceId(service), Cancel));
            Assert.Empty(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));
        }

        // Telling the screen something changed would have it read a catalogue that did not.
        Assert.Empty(events.Signalled);
    }

    private async Task<ScanApplication> ApplyAsync(ScanDifference difference)
    {
        await using var writing = database.Open();

        return await new ScanApplier(
            new BroadcastServiceRepository(writing),
            new CandidateChannelRepository(writing),
            new DatabaseAtomicWrite(writing),
            new RecordingAppEvents(),
            new StillClock()).ApplyAsync(difference, [TuneSystem.IsdbT], Cancel);
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
}
