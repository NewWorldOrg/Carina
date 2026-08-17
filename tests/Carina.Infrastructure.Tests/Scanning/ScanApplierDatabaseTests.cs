using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Scans;
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

    private static int nextNetworkId = 60_000;

    [Fact]
    public async Task EveryServiceOnOneStreamIsWrittenWithItsOwnCandidate()
    {
        var network = Interlocked.Increment(ref nextNetworkId);
        var carrying = TuningParameters.Terrestrial(PhysicalChannel);
        var measured = SignalMeasurement.WithLock(At, 21_000);
        var difference = new ScanDifference(
            [
                Change(network, 1, "First", carrying, measured),
                Change(network, 2, "Second", carrying, measured),
                Change(network, 3, "Third", carrying, measured),
            ],
            []);

        await using var writing = database.Open();
        var applied = await new ScanApplier(
            new BroadcastServiceRepository(writing),
            new CandidateChannelRepository(writing),
            new RecordingAppEvents(),
            new StillClock()).ApplyAsync(difference, [TuneSystem.IsdbT], Cancel);

        Assert.Equal(3, applied.ServicesAdded);
        Assert.Equal(3, applied.ChannelsAdded);

        await using var reading = database.Open();
        var candidates = new CandidateChannelRepository(reading);

        foreach (var service in (int[])[1, 2, 3])
        {
            var stored = Assert.Single(
                await candidates.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));

            Assert.Equal(PhysicalChannel, stored.Tuning.PhysicalChannel);
            Assert.True(stored.IsSelected);
        }
    }

    private static ScanServiceChange Change(
        int network,
        int service,
        string name,
        TuningParameters carrying,
        SignalMeasurement measured)
        => new(
            ScanChangeKind.Added,
            new NetworkId(network),
            new ServiceId(service),
            name,
            ServiceCategory.Television,
            [new ScanChannelChange(ScanChangeKind.Added, carrying, null, measured)]);
}
