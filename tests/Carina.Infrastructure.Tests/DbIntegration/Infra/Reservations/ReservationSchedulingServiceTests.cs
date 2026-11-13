using Carina.Domain.Channels;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Puppis.Contracts;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationSchedulingServiceTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const int Service = 101;

    private readonly List<ServiceProvider> standingIn = [];

    private readonly ServiceProvider container = database.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();

        foreach (ServiceProvider standing in standingIn)
        {
            await standing.DisposeAsync();
        }
    }

    [Fact]
    public async Task WhatTheSchedulerDecidedIsWhatTheLedgerHolds()
    {
        Pair channels = new(2001, 2002, Now.AddHours(3));
        Reservation lost = channels.Planned(channels.First, new Priority(10));
        Reservation kept = channels.Planned(channels.Second, new Priority(20));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            SchedulingRun first = await Scheduling(scope).CreateAsync(lost, Cancel);

            Assert.Equal(AllocationVerdict.Secured, first.Plan.For(lost.Id).Verdict);
        }

        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(lost.Id));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            SchedulingRun second = await Scheduling(scope).CreateAsync(kept, Cancel);

            Assert.Equal(AllocationVerdict.Contended, second.Plan.For(lost.Id).Verdict);
            Assert.Equal(AllocationVerdict.Secured, second.Plan.For(kept.Id).Verdict);
        }

        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(kept.Id));
        Assert.Equal(ReservationState.Conflict, await StateOfAsync(lost.Id));
    }

    [Fact]
    public async Task RecalculatingCarriesWhatChangedToTheLedgerThoughNothingIsBeingAdded()
    {
        Pair channels = new(2005, 2006, Now.AddHours(15));
        Reservation weaker = channels.Planned(channels.First, new Priority(10));
        Reservation stronger = channels.Planned(channels.Second, new Priority(20));

        await using (AsyncServiceScope scope = StandingIn(channels, seats: 2))
        {
            await Scheduling(scope).CreateAsync(weaker, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels, seats: 2))
        {
            await Scheduling(scope).CreateAsync(stronger, Cancel);
        }

        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(weaker.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(stronger.Id));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            SchedulingRun run = await Scheduling(scope).RecalculateAsync(Cancel);

            Assert.Equal(AllocationVerdict.Contended, run.Plan.For(weaker.Id).Verdict);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(weaker.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(stronger.Id));
    }

    [Fact]
    public async Task AReservationThatCannotBeInsertedTakesTheStateChangesDownWithIt()
    {
        Pair channels = new(2003, 2004, Now.AddHours(9));
        Reservation standing = channels.Planned(channels.First, new Priority(10));
        Reservation stronger = channels.Planned(channels.Second, new Priority(20));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(standing, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(stronger, Cancel);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(standing.Id));

        Reservation duplicate = ReservationFixtures.Planned(programme: stronger.Programme);

        await using (AsyncServiceScope scope = StandingIn(channels, seats: 2))
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => Scheduling(scope).CreateAsync(duplicate, Cancel));
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(standing.Id));
        Assert.Null(await FindAsync(duplicate.Id));
    }

    [Fact]
    public async Task RaisingAPriorityHandsTheSeatOverInTheSameWriteThatRecordsIt()
    {
        Pair channels = new(2007, 2008, Now.AddHours(21));
        Reservation kept = channels.Planned(channels.First, new Priority(50));
        Reservation lost = channels.Planned(channels.Second, new Priority(10));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(kept, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(lost, Cancel);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(lost.Id));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            IReservationRepository repository = Reservations(scope);
            Reservation loaded = (await repository.FindAsync(lost.Id, Cancel))!;
            SchedulingRun run = await Scheduling(scope).ReviseAsync(
                loaded,
                new ReservationRevision { Priority = new Priority(90) },
                Cancel);

            Assert.Equal(AllocationVerdict.Secured, run.Plan.For(lost.Id).Verdict);
        }

        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(lost.Id));
        Assert.Equal(ReservationState.Conflict, await StateOfAsync(kept.Id));
        Assert.Equal(90, (await FindAsync(lost.Id))!.Priority.Value);
    }

    [Fact]
    public async Task CancellingKeepsTheRowAndHandsTheSeatToWhoeverWasWaiting()
    {
        Pair channels = new(2009, 2010, Now.AddHours(27));
        Reservation kept = channels.Planned(channels.First, new Priority(50));
        Reservation lost = channels.Planned(channels.Second, new Priority(10));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(kept, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(lost, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            IReservationRepository repository = Reservations(scope);
            Reservation loaded = (await repository.FindAsync(kept.Id, Cancel))!;
            SchedulingRun run = await Scheduling(scope).ReviseAsync(
                loaded,
                new ReservationRevision { Move = ReservationMove.Cancel },
                Cancel);

            Assert.False(run.Plan.Answers(kept.Id));
            Assert.Equal(AllocationVerdict.Secured, run.Plan.For(lost.Id).Verdict);
        }

        Assert.Equal(ReservationState.Cancelled, await StateOfAsync(kept.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(lost.Id));
    }

    [Fact]
    public async Task RestoringGoesBackThroughTheCalculationRatherThanStraightToSecured()
    {
        Pair channels = new(2011, 2012, Now.AddHours(33));
        Reservation kept = channels.Planned(channels.First, new Priority(50));
        Reservation coming = channels.Planned(channels.Second, new Priority(10));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(kept, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels, seats: 2))
        {
            await Scheduling(scope).CreateAsync(coming, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels, seats: 2))
        {
            IReservationRepository repository = Reservations(scope);
            Reservation loaded = (await repository.FindAsync(coming.Id, Cancel))!;

            await Scheduling(scope).ReviseAsync(
                loaded,
                new ReservationRevision { Move = ReservationMove.Cancel },
                Cancel);
        }

        Assert.Equal(ReservationState.Cancelled, await StateOfAsync(coming.Id));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            IReservationRepository repository = Reservations(scope);
            Reservation loaded = (await repository.FindAsync(coming.Id, Cancel))!;
            SchedulingRun run = await Scheduling(scope).ReviseAsync(
                loaded,
                new ReservationRevision { Move = ReservationMove.Restore },
                Cancel);

            Assert.Equal(AllocationVerdict.Contended, run.Plan.For(coming.Id).Verdict);
            Assert.Equal([kept.Id], run.Plan.For(coming.Id).Instead);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(coming.Id));
    }

    [Fact]
    public async Task AChangeThatCannotBeSettledLeavesTheLedgerExactlyAsItWas()
    {
        Pair channels = new(2013, 2014, Now.AddHours(39));
        Reservation standing = channels.Planned(channels.First, new Priority(10));

        await using (AsyncServiceScope scope = StandingIn(channels))
        {
            await Scheduling(scope).CreateAsync(standing, Cancel);
        }

        await using (AsyncServiceScope scope = StandingIn(channels, capacity: null))
        {
            IReservationRepository repository = Reservations(scope);
            Reservation loaded = (await repository.FindAsync(standing.Id, Cancel))!;
            SchedulingRun run = await Scheduling(scope).ReviseAsync(
                loaded,
                new ReservationRevision { Priority = new Priority(90), Move = ReservationMove.Cancel },
                Cancel);

            Assert.False(run.Settled);
            Assert.Equal(SchedulingRefusal.CapacityUnknown, run.Refusal);
        }

        Reservation untouched = (await FindAsync(standing.Id))!;

        Assert.Equal(ReservationState.Scheduled, untouched.State);
        Assert.Equal(10, untouched.Priority.Value);
    }

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

        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            ICandidateChannelRepository candidates = Candidates(scope);

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

        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            await Candidates(scope).RemoveAsync(channel.Id, Cancel);
            Assert.True(await Services(scope).RemoveAsync(
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

    private AsyncServiceScope StandingIn(Pair channels, int seats = 1)
        => StandingIn(channels, Seating(seats));

    private AsyncServiceScope StandingIn(Pair channels, TunerCapacity? capacity)
    {
        TuningByService directory = new();
        directory.Answer(channels.First, TuningParameters.Terrestrial(channels.FirstChannel));
        directory.Answer(channels.Second, TuningParameters.Terrestrial(channels.SecondChannel));

        ServiceProvider standing = database.Infrastructure(replace =>
        {
            replace.AddSingleton<ITunerCapacityDirectory>(new HeldSeating(capacity));
            replace.AddSingleton<IServiceTuningDirectory>(directory);
            replace.AddSingleton<IAppEventPublisher>(new SilentEvents());
            replace.AddSingleton<TimeProvider>(new FixedClock(Now));
        });
        standingIn.Add(standing);

        return standing.CreateAsyncScope();
    }

    private static ReservationSchedulingService Scheduling(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ReservationSchedulingService>();

    private static TunerCapacity Seating(int seats)
        => new(
            [
                .. Enumerable.Range(0, seats).Select(index =>
                    new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
            ],
            []);

    private async Task<Reservation?> FindAsync(ReservationId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Reservations(scope).FindAsync(id, Cancel);
    }

    private async Task<ReservationState> StateOfAsync(ReservationId id)
        => (await FindAsync(id))!.State;

    private static IReservationRepository Reservations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRepository>();

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

    private async Task DefinedAsync(int network, params CandidateChannel[] channels)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Services(scope).AddAsync(
            BroadcastService.Discover(
                new NetworkId(network),
                new ServiceId(Service),
                "Fixture Service",
                ServiceCategory.Television,
                Now.AddDays(-30)),
            Cancel);

        ICandidateChannelRepository candidates = Candidates(scope);

        foreach (CandidateChannel channel in channels)
        {
            await candidates.AddAsync(channel, Cancel);
        }
    }

    private async Task SelectedAsync(CandidateChannel channel)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.NotNull(await Candidates(scope).SelectAsync(
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

        await using AsyncServiceScope scope = StandingIn(seats);

        SchedulingRun made = await Scheduling(scope).CreateAsync(planned, Cancel);

        Assert.Equal(AllocationVerdict.Secured, made.Plan.For(planned.Id).Verdict);

        return planned;
    }

    private async Task RecalculatedAsync(TunerCapacity seats)
    {
        await using AsyncServiceScope scope = StandingIn(seats);

        Assert.True((await Scheduling(scope).RecalculateAsync(Cancel)).Settled);
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

    private static IBroadcastServiceRepository Services(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IBroadcastServiceRepository>();

    private static ICandidateChannelRepository Candidates(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ICandidateChannelRepository>();

    private AsyncServiceScope StandingIn(TunerCapacity seats)
    {
        HeldSeating seating = new(seats);

        ServiceProvider standing = database.Infrastructure(replace =>
        {
            replace.AddSingleton<ITunerCapacityDirectory>(seating);
            replace.AddSingleton<IAppEventPublisher>(new SilentEvents());
            replace.AddSingleton<TimeProvider>(new FixedClock(Now));
        });
        standingIn.Add(standing);

        return standing.CreateAsyncScope();
    }

    private sealed record Pair(int First, int Second, DateTime StartsAt)
    {
        public int FirstChannel => 27;

        public int SecondChannel => 29;

        public Reservation Planned(int serviceId, Priority priority)
            => ReservationFixtures.Planned(
                programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId),
                priority: priority,
                startAt: StartsAt,
                endAt: StartsAt.AddHours(1));
    }
}
