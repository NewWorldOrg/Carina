using Carina.Contracts;
using Carina.Domain.Channels;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Reservations;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationSchedulingLandsInTheLedgerTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task WhatTheSchedulerDecidedIsWhatTheLedgerHolds()
    {
        Pair channels = new(2001, 2002, Now.AddHours(3));
        Reservation lost = channels.Planned(channels.First, new Priority(10));
        Reservation kept = channels.Planned(channels.Second, new Priority(20));

        await using (CarinaDbContext context = database.Open())
        {
            SchedulingRun first = await SchedulerOver(context, channels).CreateAsync(lost, Cancel);

            Assert.Equal(AllocationVerdict.Secured, first.Plan.For(lost.Id).Verdict);
        }

        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(lost.Id));

        await using (CarinaDbContext context = database.Open())
        {
            SchedulingRun second = await SchedulerOver(context, channels).CreateAsync(kept, Cancel);

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

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels, seats: 2).CreateAsync(weaker, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels, seats: 2).CreateAsync(stronger, Cancel);
        }

        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(weaker.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(stronger.Id));

        await using (CarinaDbContext context = database.Open())
        {
            SchedulingRun run = await SchedulerOver(context, channels).RecalculateAsync(Cancel);

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

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(standing, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(stronger, Cancel);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(standing.Id));

        Reservation duplicate = ReservationFixtures.Planned(programme: stronger.Programme);

        await using (CarinaDbContext context = database.Open())
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => SchedulerOver(context, channels, seats: 2).CreateAsync(duplicate, Cancel));
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

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(kept, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(lost, Cancel);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(lost.Id));

        await using (CarinaDbContext context = database.Open())
        {
            var repository = new ReservationRepository(context);
            Reservation loaded = (await repository.FindAsync(lost.Id, Cancel))!;
            SchedulingRun run = await SchedulerOver(context, channels).ReviseAsync(
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

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(kept, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(lost, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            var repository = new ReservationRepository(context);
            Reservation loaded = (await repository.FindAsync(kept.Id, Cancel))!;
            SchedulingRun run = await SchedulerOver(context, channels).ReviseAsync(
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

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(kept, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels, seats: 2).CreateAsync(coming, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            var repository = new ReservationRepository(context);
            Reservation loaded = (await repository.FindAsync(coming.Id, Cancel))!;

            await SchedulerOver(context, channels, seats: 2).ReviseAsync(
                loaded,
                new ReservationRevision { Move = ReservationMove.Cancel },
                Cancel);
        }

        Assert.Equal(ReservationState.Cancelled, await StateOfAsync(coming.Id));

        await using (CarinaDbContext context = database.Open())
        {
            var repository = new ReservationRepository(context);
            Reservation loaded = (await repository.FindAsync(coming.Id, Cancel))!;
            SchedulingRun run = await SchedulerOver(context, channels).ReviseAsync(
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

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context, channels).CreateAsync(standing, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            var repository = new ReservationRepository(context);
            Reservation loaded = (await repository.FindAsync(standing.Id, Cancel))!;
            SchedulingRun run = await SchedulerOver(context, channels, capacity: null).ReviseAsync(
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

    private static ReservationSchedulingService SchedulerOver(
        CarinaDbContext context,
        Pair channels,
        int seats = 1)
    {
        TuningByService directory = new();
        directory.Answer(channels.First, TuningParameters.Terrestrial(channels.FirstChannel));
        directory.Answer(channels.Second, TuningParameters.Terrestrial(channels.SecondChannel));

        return new ReservationSchedulingService(
            new ReservationRepository(context),
            new HeldSeating(Seating(seats)),
            directory,
            new DatabaseAtomicWrite(context),
            RollingHorizon.Default,
            new FixedClock(Now));
    }

    private static ReservationSchedulingService SchedulerOver(
        CarinaDbContext context,
        Pair channels,
        TunerCapacity? capacity)
    {
        TuningByService directory = new();
        directory.Answer(channels.First, TuningParameters.Terrestrial(channels.FirstChannel));
        directory.Answer(channels.Second, TuningParameters.Terrestrial(channels.SecondChannel));

        return new ReservationSchedulingService(
            new ReservationRepository(context),
            new HeldSeating(capacity),
            directory,
            new DatabaseAtomicWrite(context),
            RollingHorizon.Default,
            new FixedClock(Now));
    }

    private static TunerCapacity Seating(int seats)
        => new(
            [
                .. Enumerable.Range(0, seats).Select(index =>
                    new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
            ],
            []);

    private async Task<Reservation?> FindAsync(ReservationId id)
    {
        await using CarinaDbContext context = database.Open();

        return await new ReservationRepository(context).FindAsync(id, Cancel);
    }

    private async Task<ReservationState> StateOfAsync(ReservationId id)
        => (await FindAsync(id))!.State;

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
