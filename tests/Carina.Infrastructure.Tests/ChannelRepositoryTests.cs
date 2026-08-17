using Carina.Domain.Channels;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ChannelRepositoryTests(RepositoryDatabase database)
{
    private static readonly DateTime At = new(2026, 8, 14, 0, 0, 0, DateTimeKind.Utc);
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static int nextNetworkId = 50_000;

    [Fact]
    public async Task AServiceComesBackWithTheIdentifiersItWasStoredUnder()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        var services = new BroadcastServiceRepository(context);
        await services.AddAsync(Service(network, 1), Cancel);

        var found = await services.FindAsync(new NetworkId(network), new ServiceId(1), Cancel);

        Assert.NotNull(found);
        Assert.Equal("Fixture Service", found.Name);
        Assert.Equal(ServiceCategory.Television, found.Category);
        Assert.Equal(At, found.DiscoveredAt);
    }

    [Fact]
    public async Task SelectingACandidateLeavesExactlyOneSelectedForTheService()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        var services = new BroadcastServiceRepository(context);
        var candidates = new CandidateChannelRepository(context);
        await services.AddAsync(Service(network, 1), Cancel);
        var first = Candidate(network, 1, 27);
        var second = Candidate(network, 1, 28);
        await candidates.AddAsync(first, Cancel);
        await candidates.AddAsync(second, Cancel);

        await candidates.SelectAsync(
            first.Id, SelectionSource.Manual, SignalMeasurement.WithLock(At, 21_000), At, Cancel);
        await candidates.SelectAsync(
            second.Id, SelectionSource.AutoSwitch, SignalMeasurement.WithLock(At, 22_000), At, Cancel);

        await using var reading = database.Open();
        var stored = await new CandidateChannelRepository(reading)
            .ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel);

        var selected = Assert.Single(stored, candidate => candidate.IsSelected);
        Assert.Equal(second.Id, selected.Id);
        Assert.Equal(SelectionSource.AutoSwitch, selected.SelectionSource);
        Assert.Equal(22_000, selected.SelectionMeasurement?.CnrMilliDecibels);
    }

    [Fact]
    public async Task ClearingTheSelectionLeavesTheServiceWithNoWayToTuneItAndNoRepair()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        await new BroadcastServiceRepository(context).AddAsync(Service(network, 1), Cancel);
        var candidates = new CandidateChannelRepository(context);
        var only = Candidate(network, 1, 27);
        await candidates.AddAsync(only, Cancel);
        await candidates.SelectAsync(
            only.Id, SelectionSource.Manual, SignalMeasurement.WithLock(At, 21_000), At, Cancel);

        await candidates.ClearSelectionAsync(new NetworkId(network), new ServiceId(1), Cancel);

        await using var reading = database.Open();
        var repository = new CandidateChannelRepository(reading);
        Assert.Null(await repository.FindSelectedAsync(new NetworkId(network), new ServiceId(1), Cancel));
        var left = Assert.Single(
            await repository.ListForServiceAsync(new NetworkId(network), new ServiceId(1), Cancel));

        // The reading taken at selection has to leave the row rather than linger in columns
        // nothing reads back: an unselected candidate carrying one would rank as measured.
        Assert.Null(left.SelectionMeasurement);
        Assert.Null(left.LastMeasurement);
    }

    [Fact]
    public async Task ACandidateOutOfRotationIsLeftOutOfTheRoundAndStaysVisible()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        await new BroadcastServiceRepository(context).AddAsync(Service(network, 1), Cancel);
        var candidates = new CandidateChannelRepository(context);
        var backingOff = Candidate(network, 1, 27);
        var lost = Candidate(network, 1, 28);
        await candidates.AddAsync(backingOff, Cancel);
        await candidates.AddAsync(lost, Cancel);

        var backoff = new RotationBackoff(TimeSpan.FromMinutes(1), 2, TimeSpan.FromHours(1), 2);
        backingOff.RecordTuningFailure(backoff, At);
        lost.RecordTuningFailure(backoff, At);
        lost.RecordTuningFailure(backoff, At);
        await candidates.SaveAsync(backingOff, Cancel);
        await candidates.SaveAsync(lost, Cancel);

        await using var reading = database.Open();
        var repository = new CandidateChannelRepository(reading);
        var inRotation = await repository.ListInRotationAsync(At.AddMinutes(2), Cancel);
        var needingAttention = await repository.ListNeedingAttentionAsync(Cancel);

        Assert.Contains(inRotation, candidate => candidate.Id.Equals(backingOff.Id));
        Assert.DoesNotContain(inRotation, candidate => candidate.Id.Equals(lost.Id));
        Assert.Contains(needingAttention, candidate => candidate.Id.Equals(lost.Id));
        Assert.Equal(At, needingAttention.Single(candidate => candidate.Id.Equals(lost.Id)).NeedsAttentionSince);
    }

    [Fact]
    public async Task ACandidateBackingOffIsSkippedUntilItsNextAttemptIsDue()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        await new BroadcastServiceRepository(context).AddAsync(Service(network, 1), Cancel);
        var candidates = new CandidateChannelRepository(context);
        var candidate = Candidate(network, 1, 27);
        await candidates.AddAsync(candidate, Cancel);
        candidate.RecordTuningFailure(RotationBackoff.Default, At);
        await candidates.SaveAsync(candidate, Cancel);

        await using var reading = database.Open();
        var repository = new CandidateChannelRepository(reading);

        Assert.DoesNotContain(
            await repository.ListInRotationAsync(At, Cancel),
            stored => stored.Id.Equals(candidate.Id));
        Assert.Contains(
            await repository.ListInRotationAsync(At.AddHours(2), Cancel),
            stored => stored.Id.Equals(candidate.Id));
    }

    [Fact]
    public async Task RecordingAFailureDoesNotUndoASelectionMadeMeanwhile()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        await new BroadcastServiceRepository(context).AddAsync(Service(network, 1), Cancel);
        var candidate = Candidate(network, 1, 27);
        await new CandidateChannelRepository(context).AddAsync(candidate, Cancel);

        await using var elsewhere = database.Open();
        await new CandidateChannelRepository(elsewhere)
            .SelectAsync(candidate.Id, SelectionSource.AutoSwitch, null, At, Cancel);

        candidate.RecordTuningFailure(RotationBackoff.Default, At.AddMinutes(1));
        await using var stale = database.Open();
        await new CandidateChannelRepository(stale).SaveAsync(candidate, Cancel);

        await using var reading = database.Open();
        var stored = await new CandidateChannelRepository(reading).FindAsync(candidate.Id, Cancel);
        Assert.True(stored!.IsSelected);
        Assert.Equal(SelectionSource.AutoSwitch, stored.SelectionSource);
        Assert.Equal(RotationState.BackingOff, stored.RotationState);
    }

    [Fact]
    public async Task AChangedTunerLedgerMarksEveryCandidateForRevalidation()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        await new BroadcastServiceRepository(context).AddAsync(Service(network, 1), Cancel);
        var candidates = new CandidateChannelRepository(context);
        var candidate = Candidate(network, 1, 27);
        await candidates.AddAsync(candidate, Cancel);

        await candidates.RequireRevalidationAsync(Cancel);

        await using var reading = database.Open();
        var stored = await new CandidateChannelRepository(reading).FindAsync(candidate.Id, Cancel);
        Assert.True(stored!.NeedsRevalidation);
    }

    [Fact]
    public async Task DeletingAServiceTakesItsCandidatesAndNothingElse()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        var services = new BroadcastServiceRepository(context);
        var candidates = new CandidateChannelRepository(context);
        await services.AddAsync(Service(network, 1), Cancel);
        await services.AddAsync(Service(network, 2), Cancel);
        var doomed = Candidate(network, 1, 27);
        var spared = Candidate(network, 2, 27);
        await candidates.AddAsync(doomed, Cancel);
        await candidates.AddAsync(spared, Cancel);

        await services.RemoveAsync(new NetworkId(network), new ServiceId(1), Cancel);

        await using var reading = database.Open();
        var repository = new CandidateChannelRepository(reading);
        Assert.Null(await repository.FindAsync(doomed.Id, Cancel));
        Assert.NotNull(await repository.FindAsync(spared.Id, Cancel));
    }

    [Fact]
    public async Task ServicesSharingOneChannelEachKeepACandidateOfTheirOwn()
    {
        var network = NextNetwork();
        await using var context = database.Open();
        var services = new BroadcastServiceRepository(context);
        var candidates = new CandidateChannelRepository(context);
        await services.AddAsync(Service(network, 1), Cancel);
        await services.AddAsync(Service(network, 2), Cancel);

        var carrying = TuningParameters.Terrestrial(27);
        var measured = SignalMeasurement.WithLock(At, 21_000);

        foreach (var service in (int[])[1, 2])
        {
            var candidate = CandidateChannel.Discover(
                CandidateChannelId.New(),
                new NetworkId(network),
                new ServiceId(service),
                carrying,
                At);
            candidate.RecordTuningSuccess(measured, At);

            await candidates.AddAsync(candidate, Cancel);
        }

        await using var reading = database.Open();
        var repository = new CandidateChannelRepository(reading);

        foreach (var service in (int[])[1, 2])
        {
            var stored = Assert.Single(
                await repository.ListForServiceAsync(new NetworkId(network), new ServiceId(service), Cancel));

            Assert.Equal(27, stored.Tuning.PhysicalChannel);
            Assert.Equal(21_000, stored.LastMeasurement?.CnrMilliDecibels);
        }
    }

    [Fact]
    public async Task TheSatelliteReferenceRowsAreReplacedOneSlotAtATime()
    {
        await using var context = database.Open();
        var streams = new SatelliteTransportStreamRepository(context);

        var seeded = await streams.ListAsync(Cancel);
        Assert.Equal(10, seeded.Count);

        await streams.ReplaceSlotAsync(
            15,
            [
                SatelliteTransportStream.Rehydrate(15, 0, new TransportStreamId(0x40F0)),
                SatelliteTransportStream.Rehydrate(15, 1, new TransportStreamId(0x40F1)),
            ],
            Cancel);

        await using var reading = database.Open();
        var slot = await new SatelliteTransportStreamRepository(reading)
            .ListForSlotAsync(15, Cancel);

        Assert.Equal(2, slot.Count);
        Assert.Equal(11, (await new SatelliteTransportStreamRepository(reading).ListAsync(Cancel)).Count);
    }

    [Fact]
    public async Task ASatelliteSlotReplacedInsideALargerWriteGoesBackWithIt()
    {
        const int Slot = 21;

        await using var context = database.Open();
        var streams = new SatelliteTransportStreamRepository(context);
        var before = await streams.ListForSlotAsync(Slot, Cancel);

        await Assert.ThrowsAsync<StoreRefusedException>(
            () => new DatabaseAtomicWrite(context).AllOrNothingAsync<int>(
                async token =>
                {
                    await streams.ReplaceSlotAsync(
                        Slot,
                        [SatelliteTransportStream.Rehydrate(Slot, 0, new TransportStreamId(0x40F0))],
                        token);

                    throw new StoreRefusedException("whatever ends the write after the slot");
                },
                Cancel));

        // The slot replacement owns no boundary of its own inside a larger write, so it goes
        // back with it rather than standing while the rest is undone.
        await using var reading = database.Open();
        var after = await new SatelliteTransportStreamRepository(reading).ListForSlotAsync(Slot, Cancel);

        Assert.Equal(
            before.Select(stream => stream.TransportStreamId),
            after.Select(stream => stream.TransportStreamId));
    }

    private static int NextNetwork() => Interlocked.Increment(ref nextNetworkId);

    private static BroadcastService Service(int network, int service)
        => BroadcastService.Discover(
            new NetworkId(network),
            new ServiceId(service),
            "Fixture Service",
            ServiceCategory.Television,
            At);

    private static CandidateChannel Candidate(int network, int service, int physicalChannel)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(network),
            new ServiceId(service),
            TuningParameters.Terrestrial(physicalChannel),
            At);
}
