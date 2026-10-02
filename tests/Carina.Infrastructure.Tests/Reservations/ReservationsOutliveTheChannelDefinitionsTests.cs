using Carina.Contracts;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Channels;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Reservations;
using Carina.TestSupport;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationsOutliveTheChannelDefinitionsTests(RepositoryDatabase database)
{
    private const int Service = 101;

    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a reservation is still there, and still secured, after the service is switched to another of its channels")]
    public async Task AReservationIsStillThereAfterTheServiceIsSwitchedToAnotherOfItsChannels()
    {
        int network = BroadcastIds.NextNetwork();
        CandidateChannel first = Terrestrial(network, 27);
        CandidateChannel second = Terrestrial(network, 28);

        await DefinedAsync(network, first, second);
        await SelectedAsync(first);

        Reservation made = await ReservedAsync(network, Seats(TunerKind.Terrestrial), hoursAhead: 51);

        await SelectedAsync(second);
        await RecalculatedAsync(Seats(TunerKind.Terrestrial));

        await StandsAsItWasMadeAsync(made, ReservationState.Scheduled);
    }

    [Fact(DisplayName = "a reservation is still there after every tuner that could receive it is turned off, and is secured again when one comes back")]
    public async Task AReservationIsStillThereAfterEveryTunerThatCouldReceiveItIsTurnedOff()
    {
        int network = BroadcastIds.NextNetwork();
        CandidateChannel channel = Terrestrial(network, 27);

        await DefinedAsync(network, channel);
        await SelectedAsync(channel);

        Reservation made = await ReservedAsync(network, Seats(TunerKind.Terrestrial), hoursAhead: 54);

        await RecalculatedAsync(Seats());

        Reservation without = (await FindAsync(made.Id))!;

        Assert.NotEqual(ReservationState.Cancelled, without.State);
        Assert.True(without.ReceptionUnavailable);
        Assert.Equal(made.SnapshotName, without.SnapshotName);

        await RecalculatedAsync(Seats(TunerKind.Terrestrial));

        await StandsAsItWasMadeAsync(made, ReservationState.Scheduled);
    }

    [Fact(DisplayName = "a reservation is still there, and still secured, after a satellite reorganisation moves its service to another transport stream")]
    public async Task AReservationIsStillThereAfterItsServiceMovesToAnotherTransportStream()
    {
        int network = BroadcastIds.NextNetwork();
        CandidateChannel before = Satellite(network, new TransportStreamId(16_625));

        await DefinedAsync(network, before);
        await SelectedAsync(before);

        Reservation made = await ReservedAsync(network, Seats(TunerKind.Satellite), hoursAhead: 57);

        CandidateChannel after = Satellite(network, new TransportStreamId(16_626));

        await using (CarinaDbContext context = database.Open())
        {
            var candidates = new CandidateChannelRepository(context);

            await candidates.RemoveAsync(before.Id, Cancel);
            await candidates.AddAsync(after, Cancel);
        }

        await SelectedAsync(after);
        await RecalculatedAsync(Seats(TunerKind.Satellite));

        await StandsAsItWasMadeAsync(made, ReservationState.Scheduled);
    }

    [Fact(DisplayName = "a reservation is still there after the channel definition it was made on is deleted, and is secured again when the definition is back")]
    public async Task AReservationIsStillThereAfterTheChannelDefinitionItWasMadeOnIsDeleted()
    {
        int network = BroadcastIds.NextNetwork();
        CandidateChannel channel = Terrestrial(network, 27);

        await DefinedAsync(network, channel);
        await SelectedAsync(channel);

        Reservation made = await ReservedAsync(network, Seats(TunerKind.Terrestrial), hoursAhead: 60);

        await using (CarinaDbContext context = database.Open())
        {
            await new CandidateChannelRepository(context).RemoveAsync(channel.Id, Cancel);
            Assert.True(await new BroadcastServiceRepository(context).RemoveAsync(
                new NetworkId(network),
                new ServiceId(Service),
                Cancel));
        }

        await RecalculatedAsync(Seats(TunerKind.Terrestrial));

        Reservation without = (await FindAsync(made.Id))!;

        Assert.NotEqual(ReservationState.Cancelled, without.State);
        Assert.True(without.ReceptionUnavailable);
        Assert.Equal(made.SnapshotName, without.SnapshotName);
        Assert.Equal(made.Priority, without.Priority);

        CandidateChannel again = Terrestrial(network, 27);

        await DefinedAsync(network, again);
        await SelectedAsync(again);
        await RecalculatedAsync(Seats(TunerKind.Terrestrial));

        await StandsAsItWasMadeAsync(made, ReservationState.Scheduled);
    }

    private static CandidateChannel Terrestrial(int network, int physicalChannel)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(network),
            new ServiceId(Service),
            TuningParameters.Terrestrial(physicalChannel),
            Now.AddDays(-30));

    private static CandidateChannel Satellite(int network, TransportStreamId stream)
        => CandidateChannel.Discover(
            CandidateChannelId.New(),
            new NetworkId(network),
            new ServiceId(Service),
            TuningParameters.Bs(15, stream),
            Now.AddDays(-30));

    private static TunerCapacity Seats(params TunerKind[] kinds)
        => new(
            [
                .. kinds.Select((kind, index) =>
                    new TunerSeat($"seat{index}", BroadcastReception.Of(kind), Faulted: false)),
            ],
            []);

    private static ReservationSchedulingService SchedulerOver(CarinaDbContext context, TunerCapacity seats)
    {
        HeldSeating seating = new(seats);

        return new ReservationSchedulingService(
            new ReservationRepository(context),
            new RecordingRepository(context),
            seating,
            new ServiceTuningDirectory(
                new BroadcastServiceRepository(context),
                new CandidateChannelRepository(context),
                seating),
            new DatabaseAtomicWrite(context),
            RollingHorizon.Default,
            new SilentEvents(),
            new FixedClock(Now));
    }

    private async Task DefinedAsync(int network, params CandidateChannel[] channels)
    {
        await using CarinaDbContext context = database.Open();

        await new BroadcastServiceRepository(context).AddAsync(
            BroadcastService.Discover(
                new NetworkId(network),
                new ServiceId(Service),
                "Fixture Service",
                ServiceCategory.Television,
                Now.AddDays(-30)),
            Cancel);

        var candidates = new CandidateChannelRepository(context);

        foreach (CandidateChannel channel in channels)
        {
            await candidates.AddAsync(channel, Cancel);
        }
    }

    private async Task SelectedAsync(CandidateChannel channel)
    {
        await using CarinaDbContext context = database.Open();

        Assert.NotNull(await new CandidateChannelRepository(context).SelectAsync(
            channel.Id,
            SelectionSource.Manual,
            SignalMeasurement.WithLock(Now, 21_000),
            Now,
            Cancel));
    }

    private async Task<Reservation> ReservedAsync(int network, TunerCapacity seats, int hoursAhead)
    {
        Reservation planned = ReservationFixtures.Planned(
            programme: new ProgrammeRef(
                new NetworkId(network),
                new ServiceId(Service),
                new EventId(ReservationFixtures.NextEventId()),
                Now.AddHours(hoursAhead)),
            priority: new Priority(40));

        await using CarinaDbContext context = database.Open();

        SchedulingRun made = await SchedulerOver(context, seats).CreateAsync(planned, Cancel);

        Assert.Equal(AllocationVerdict.Secured, made.Plan.For(planned.Id).Verdict);

        return planned;
    }

    private async Task RecalculatedAsync(TunerCapacity seats)
    {
        await using CarinaDbContext context = database.Open();

        Assert.True((await SchedulerOver(context, seats).RecalculateAsync(Cancel)).Settled);
    }

    private async Task<Reservation?> FindAsync(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new ReservationRepository(context).FindAsync(id, Cancel);
    }

    private async Task StandsAsItWasMadeAsync(Reservation made, ReservationState state)
    {
        Reservation found = (await FindAsync(made.Id))!;

        Assert.Equal(state, found.State);
        Assert.False(found.ReceptionUnavailable);
        Assert.Equal(made.Programme, found.Programme);
        Assert.Equal(made.Priority, found.Priority);
        Assert.Equal(made.StartAt, found.StartAt);
        Assert.Equal(made.EndAt, found.EndAt);
        Assert.Equal(made.SnapshotName, found.SnapshotName);
        Assert.Equal(made.CreatedAt, found.CreatedAt);
    }
}
