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
            new HeldSeating(new TunerCapacity(
                [
                    .. Enumerable.Range(0, seats).Select(index =>
                        new TunerSeat($"seat{index}", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)),
                ],
                [])),
            directory,
            new DatabaseAtomicWrite(context),
            RollingHorizon.Default,
            new FixedClock(Now));
    }

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
