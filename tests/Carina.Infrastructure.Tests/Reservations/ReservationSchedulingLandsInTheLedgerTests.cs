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
    private const int Kept = 2001;

    private const int Lost = 2002;

    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task WhatTheSchedulerDecidedIsWhatTheLedgerHolds()
    {
        Reservation kept = OnAChannelOfItsOwn(Kept, new Priority(20));
        Reservation lost = OnAChannelOfItsOwn(Lost, new Priority(10));

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context).CreateAsync(kept, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            SchedulingRun run = await SchedulerOver(context).CreateAsync(lost, Cancel);

            Assert.Equal(AllocationVerdict.Contended, run.Plan.For(lost.Id).Verdict);
        }

        await using CarinaDbContext reading = database.Open();
        var ledger = new ReservationRepository(reading);

        Assert.Equal(ReservationState.Scheduled, (await ledger.FindAsync(kept.Id, Cancel))!.State);
        Assert.Equal(ReservationState.Conflict, (await ledger.FindAsync(lost.Id, Cancel))!.State);
    }

    [Fact]
    public async Task AReservationThatCannotBeInsertedTakesTheStateChangesDownWithIt()
    {
        Reservation first = OnAChannelOfItsOwn(Kept, new Priority(20));
        Reservation second = OnAChannelOfItsOwn(Lost, new Priority(10));

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context).CreateAsync(first, Cancel);
        }

        await using (CarinaDbContext context = database.Open())
        {
            await SchedulerOver(context).CreateAsync(second, Cancel);
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(second.Id));

        Reservation duplicate = ReservationFixtures.Planned(programme: first.Programme);

        await using (CarinaDbContext context = database.Open())
        {
            await Assert.ThrowsAsync<DbUpdateException>(
                () => SchedulerOver(context, secondSeat: true).CreateAsync(duplicate, Cancel));
        }

        Assert.Equal(ReservationState.Conflict, await StateOfAsync(second.Id));
        Assert.Null(await FindAsync(duplicate.Id));
    }

    private static Reservation OnAChannelOfItsOwn(int serviceId, Priority priority)
        => ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId),
            priority: priority,
            startAt: Now.AddHours(3),
            endAt: Now.AddHours(4));

    private static ReservationSchedulingService SchedulerOver(CarinaDbContext context, bool secondSeat = false)
    {
        TuningByService directory = new();
        directory.Answer(Kept, TuningParameters.Terrestrial(27));
        directory.Answer(Lost, TuningParameters.Terrestrial(29));

        TunerSeat[] seats = secondSeat
            ?
            [
                new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
                new TunerSeat("seat1", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false),
            ]
            : [new TunerSeat("seat0", BroadcastReception.Of(TunerKind.Terrestrial), Faulted: false)];

        return new ReservationSchedulingService(
            new ReservationRepository(context),
            new HeldSeating(new TunerCapacity(seats, [])),
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
}
