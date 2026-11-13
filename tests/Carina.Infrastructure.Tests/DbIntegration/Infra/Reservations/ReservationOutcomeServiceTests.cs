using Carina.Domain.Base;
using Carina.Domain.Events;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Tests.Fixtures.Reservations;
using Carina.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Infra.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationOutcomeServiceTests(RepositoryDatabase database) : IAsyncLifetime
{
    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);

    private static readonly DateTime LongBefore = ReservationFixtures.Now.AddYears(-1);

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
    public async Task WhatLostTheContestLandsWithTheNameOfWhatWasRecordedInstead()
    {
        DateTime opens = LongBefore;
        Reservation lost = await LaidDownAsync(ReservationState.Conflict, opens, opens.AddHours(1));
        Reservation won = await LaidDownAsync(
            ReservationState.Scheduled,
            opens.AddMinutes(30),
            opens.AddMinutes(90),
            claimedAt: opens.AddMinutes(30),
            outcome: RecordingOutcome.Complete);
        Reservation elsewhere = await LaidDownAsync(
            ReservationState.Scheduled,
            opens.AddHours(5),
            opens.AddHours(6),
            claimedAt: opens.AddHours(5),
            outcome: RecordingOutcome.Complete);

        ReservationOutcomeRun run = await RecordingAsync(opens.AddHours(2));

        Assert.Contains(new ReservationOutcomeRecord(lost.Id, ReservationOutcomeKind.Competing), run.Recorded);

        ReservationOutcome held = Assert.Single(await ForAsync(lost.Id));

        Assert.Equal(ReservationOutcomeKind.Competing, held.Kind);
        Assert.Equal([won.Id.Value], held.RecordedInstead);
        Assert.Equal(lost.EffectiveStartAt, held.EffectiveStartAt);
        Assert.Equal(lost.EffectiveEndAt, held.EffectiveEndAt);
        Assert.Equal(lost.SnapshotName, held.SnapshotName);
        Assert.Equal(lost.Priority, held.Priority);
        Assert.Equal(opens.AddHours(2), held.OccurredAt);
        Assert.Null(held.TuneFailure);
        Assert.Null(held.RecordingOutcome);
        Assert.Empty(await ForAsync(won.Id));
        Assert.Empty(await ForAsync(elsewhere.Id));
        Assert.Equal(ReservationState.Missed, await StateOfAsync(lost.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(won.Id));
    }

    [Fact]
    public async Task AReservationNothingClaimedLandsAsMissedAndStopsSayingItIsSecured()
    {
        DateTime opens = LongBefore.AddHours(10);
        Reservation gone = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        Assert.Contains(
            new ReservationOutcomeRecord(gone.Id, ReservationOutcomeKind.Missed),
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        Assert.Equal(ReservationOutcomeKind.Missed, Assert.Single(await ForAsync(gone.Id)).Kind);
        Assert.Equal(ReservationState.Missed, await StateOfAsync(gone.Id));
    }

    [Fact]
    public async Task AClaimNoRecordingCameOfIsLetGoOfSoTheReservationStopsSayingItIsRecording()
    {
        DateTime opens = LongBefore.AddHours(80);
        Reservation stranded = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);

        Assert.Equal(ReservationStanding.Recording, await StandingOfAsync(stranded.Id));

        Assert.Contains(
            new ReservationOutcomeRecord(stranded.Id, ReservationOutcomeKind.Missed),
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        Assert.Equal(ReservationOutcomeKind.Missed, Assert.Single(await ForAsync(stranded.Id)).Kind);
        Assert.Equal(ReservationState.Missed, await StateOfAsync(stranded.Id));
        Assert.Equal(ReservationStanding.Missed, await StandingOfAsync(stranded.Id));
    }

    [Fact]
    public async Task AClaimARecordingIsBeingWrittenUnderIsNotTakenAwayFromIt()
    {
        DateTime opens = LongBefore.AddHours(90);
        Reservation running = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);

        await WritingAsync(running, opens);

        Assert.Empty((await RecordingAsync(opens.AddHours(2))).Recorded);
        Assert.Empty(await ForAsync(running.Id));
        Assert.Equal(ReservationStanding.Recording, await StandingOfAsync(running.Id));
    }

    [Fact]
    public async Task ARecordingTheLedgerSaysFailedLandsWithoutMovingTheReservation()
    {
        DateTime opens = LongBefore.AddHours(20);
        Reservation broke = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens,
            outcome: RecordingOutcome.Failed);

        Assert.Contains(
            new ReservationOutcomeRecord(broke.Id, ReservationOutcomeKind.RecordingFailure),
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        ReservationOutcome held = Assert.Single(await ForAsync(broke.Id));

        Assert.Equal(ReservationOutcomeKind.RecordingFailure, held.Kind);
        Assert.Equal(RecordingOutcome.Failed, held.RecordingOutcome);
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(broke.Id));
    }

    [Fact]
    public async Task ARecordingThatCameOutWholeButScrambledLandsAsAFailureOfTheRecording()
    {
        DateTime opens = LongBefore.AddHours(24);
        Reservation scrambled = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);
        await EndedWholeAsync(scrambled, opens, RecordingFault.ScramblingUnresolved);

        Assert.Contains(
            new ReservationOutcomeRecord(scrambled.Id, ReservationOutcomeKind.RecordingFailure),
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        ReservationOutcome held = Assert.Single(await ForAsync(scrambled.Id));

        Assert.Equal(ReservationOutcomeKind.RecordingFailure, held.Kind);
        Assert.Equal(RecordingOutcome.Complete, held.RecordingOutcome);
        Assert.Equal([RecordingFault.ScramblingUnresolved], held.Faults);
        Assert.True(held.LeftScrambled);
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(scrambled.Id));
        Assert.Empty((await RecordingAsync(opens.AddHours(3))).Recorded);
        Assert.Single(await ForAsync(scrambled.Id));
    }

    [Fact]
    public async Task ARecordingThatCameOutWholeAndClearIsNotWrittenDown()
    {
        DateTime opens = LongBefore.AddHours(28);
        Reservation clear = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);
        await EndedWholeAsync(clear, opens, RecordingFault.HeavierThanTheStream);

        Assert.DoesNotContain(
            (await RecordingAsync(opens.AddHours(2))).Recorded,
            recorded => recorded.Reservation.Equals(clear.Id));
        Assert.Empty(await ForAsync(clear.Id));
    }

    [Fact]
    public async Task ARecordingAlreadyDescrambledIsNotWrittenDown()
    {
        DateTime opens = LongBefore.AddHours(32);
        Reservation descrambled = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);
        Recording recording = await EndedWholeAsync(descrambled, opens, RecordingFault.ScramblingUnresolved);

        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            IRecordingRepository recordings = Recordings(scope);
            Recording found = (await recordings.FindAsync(recording.Id, Cancel))!;
            found.Descrambled(opens.AddHours(1).AddMinutes(30));
            await recordings.SaveAsync(found, Cancel);
        }

        Assert.DoesNotContain(
            (await RecordingAsync(opens.AddHours(2))).Recorded,
            recorded => recorded.Reservation.Equals(descrambled.Id));
        Assert.Empty(await ForAsync(descrambled.Id));
    }

    [Fact]
    public async Task ALineLeftScrambledIsDescrambledAndReadBackThatWay()
    {
        DateTime opens = LongBefore.AddHours(36);
        Reservation scrambled = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);
        await EndedWholeAsync(scrambled, opens, RecordingFault.ScramblingUnresolved);
        await RecordingAsync(opens.AddHours(2));
        ReservationOutcome line = Assert.Single(await ForAsync(scrambled.Id));

        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            ReservationOutcome held = await Context(scope).Set<ReservationOutcome>().SingleAsync(
                outcome => outcome.Id == line.Id,
                Cancel);
            held.Descrambled(opens.AddDays(1));
            await Context(scope).SaveChangesAsync(Cancel);
        }

        ReservationOutcome again = Assert.Single(await ForAsync(scrambled.Id));

        Assert.Equal(opens.AddDays(1), again.DescrambledAt);
        Assert.False(again.LeftScrambled);
        Assert.Equal([RecordingFault.ScramblingUnresolved], again.Faults);
    }

    [Fact]
    public async Task AReservationStillInsideItsWindowIsNotOfferedUntilItCloses()
    {
        DateTime opens = LongBefore.AddHours(100);
        Reservation running = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        Assert.DoesNotContain(
            new ReservationOutcomeRecord(running.Id, ReservationOutcomeKind.Missed),
            (await RecordingAsync(opens.AddMinutes(59))).Recorded);
        Assert.Empty(await ForAsync(running.Id));
        Assert.Equal(ReservationState.Scheduled, await StateOfAsync(running.Id));

        Assert.Contains(
            new ReservationOutcomeRecord(running.Id, ReservationOutcomeKind.Missed),
            (await RecordingAsync(opens.AddHours(1))).Recorded);
        Assert.Equal(ReservationState.Missed, await StateOfAsync(running.Id));
    }

    [Fact]
    public async Task AReservationTheLedgerAlreadyHoldsIsNotOfferedAgain()
    {
        DateTime opens = LongBefore.AddHours(30);
        Reservation broke = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens,
            outcome: RecordingOutcome.Failed);

        Assert.Contains(
            new ReservationOutcomeRecord(broke.Id, ReservationOutcomeKind.RecordingFailure),
            (await RecordingAsync(opens.AddHours(2))).Recorded);
        Assert.DoesNotContain(
            new ReservationOutcomeRecord(broke.Id, ReservationOutcomeKind.RecordingFailure),
            (await RecordingAsync(opens.AddHours(3))).Recorded);
        Assert.Single(await ForAsync(broke.Id));

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReadOnlyList<ReservationAwaitingOutcome> offered =
            await Reservations(scope).ListAwaitingOutcomeAsync(opens.AddHours(3), Cancel);

        Assert.DoesNotContain(broke.Id.Value, offered.Select(one => one.Reservation.Id.Value));
    }

    [Fact]
    public async Task TheTableItselfRefusesASecondRowOfTheSameKind()
    {
        DateTime opens = LongBefore.AddHours(50);
        Reservation gone = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        await AddAsync(gone, ReservationOutcomeKind.Missed, opens.AddHours(2));

        await Assert.ThrowsAsync<DbUpdateException>(
            () => AddAsync(gone, ReservationOutcomeKind.Missed, opens.AddHours(3)));
    }

    [Fact]
    public async Task TheTableTakesASecondRowOfADifferentKind()
    {
        DateTime opens = LongBefore.AddHours(60);
        Reservation gone = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        await AddAsync(gone, ReservationOutcomeKind.Missed, opens.AddHours(2));
        await AddAsync(gone, ReservationOutcomeKind.Competing, opens.AddHours(3));

        Assert.Equal(
            [ReservationOutcomeKind.Competing, ReservationOutcomeKind.Missed],
            [.. (await ForAsync(gone.Id)).Select(outcome => outcome.Kind).Order()]);
    }

    [Fact]
    public async Task WhatWasClaimedOverAWindowIsWhatTheQueryHandsBack()
    {
        DateTime opens = LongBefore.AddHours(70);
        Reservation claimed = await LaidDownAsync(
            ReservationState.Scheduled,
            opens,
            opens.AddHours(1),
            claimedAt: opens);
        Reservation never = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));
        Reservation far = await LaidDownAsync(
            ReservationState.Scheduled,
            opens.AddHours(10),
            opens.AddHours(11),
            claimedAt: opens.AddHours(10));

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReadOnlyList<Reservation> over = await Reservations(scope).ListClaimedOverAsync(
            new ReservationWindow(opens.AddMinutes(-1), opens.AddMinutes(1)),
            Cancel);
        List<Guid> found = [.. over.Select(reservation => reservation.Id.Value)];

        Assert.Contains(claimed.Id.Value, found);
        Assert.DoesNotContain(never.Id.Value, found);
        Assert.DoesNotContain(far.Id.Value, found);
    }

    [Fact]
    public async Task AReservationThatFollowedItsProgrammeIsStillOfferedOnceItsWindowHasClosed()
    {
        DateTime opens = LongBefore.AddHours(110);
        Reservation followed = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));

        await AddAsync(followed, ReservationOutcomeKind.ProgrammeMoved, opens.AddMinutes(-30));

        await using (AsyncServiceScope scope = container.CreateAsyncScope())
        {
            IReadOnlyList<ReservationAwaitingOutcome> offered =
                await Reservations(scope).ListAwaitingOutcomeAsync(opens.AddHours(2), Cancel);

            Assert.Contains(followed.Id.Value, offered.Select(one => one.Reservation.Id.Value));
        }

        Assert.Contains(
            new ReservationOutcomeRecord(followed.Id, ReservationOutcomeKind.Missed),
            (await RecordingAsync(opens.AddHours(2))).Recorded);

        Assert.Equal(
            [ReservationOutcomeKind.Missed, ReservationOutcomeKind.ProgrammeMoved],
            [.. (await ForAsync(followed.Id)).Select(outcome => outcome.Kind).Order()]);
        Assert.Equal(ReservationState.Missed, await StateOfAsync(followed.Id));
    }

    [Fact]
    public async Task AReservationChangedElsewhereMidPassCostsOnlyThatStageAndTheNextOneOnTheSameContextLandsBoth()
    {
        DateTime opens = LongBefore.AddHours(200);
        Reservation changed = await LaidDownAsync(ReservationState.Scheduled, opens, opens.AddHours(1));
        Reservation after = await LaidDownAsync(ReservationState.Scheduled, opens.AddHours(1), opens.AddHours(2));
        DateTime at = opens.AddHours(3);

        await using AsyncServiceScope pass = StandingIn(
            at,
            outcomes => new MeddlingOutcomes(
                outcomes,
                changed.Id,
                async () =>
                {
                    await using AsyncServiceScope elsewhere = container.CreateAsyncScope();
                    IReservationRepository repository = Reservations(elsewhere);
                    Reservation found = (await repository.FindAsync(changed.Id, Cancel))!;
                    found.Reprioritise(new Priority(55));
                    await repository.SaveAllAsync([found], Cancel);
                }));

        await Assert.ThrowsAsync<ReservationMovedMeanwhileException>(
            () => OutcomeService(pass).RecordAsync(Cancel));

        ReservationOutcomeRun next = await OutcomeService(pass).RecordAsync(Cancel);

        Assert.Contains(new ReservationOutcomeRecord(changed.Id, ReservationOutcomeKind.Missed), next.Recorded);
        Assert.Contains(new ReservationOutcomeRecord(after.Id, ReservationOutcomeKind.Missed), next.Recorded);
        Assert.Equal(ReservationState.Missed, await StateOfAsync(changed.Id));
        Assert.Equal(ReservationState.Missed, await StateOfAsync(after.Id));
    }

    /// <summary>
    /// The container over the scratch database with the clock at <paramref name="at"/>, the grace this class
    /// works to, and, when <paramref name="outcomes"/> is given, the outcomes wrapped by it.
    /// </summary>
    private AsyncServiceScope StandingIn(
        DateTime at,
        Func<IReservationOutcomeRepository, IReservationOutcomeRepository>? outcomes = null)
    {
        ServiceProvider standing = database.Infrastructure(replace =>
        {
            replace.AddSingleton(new ReservationOutcomeSettings { Grace = Grace });
            replace.AddSingleton<IAppEventPublisher>(new SilentEvents());
            replace.AddSingleton<TimeProvider>(new FixedClock(at));

            if (outcomes is not null)
            {
                RegisteredServices.Wrap(replace, outcomes);
            }
        });
        standingIn.Add(standing);

        return standing.CreateAsyncScope();
    }

    private static ReservationOutcomeService OutcomeService(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<ReservationOutcomeService>();

    private async Task<ReservationOutcomeRun> RecordingAsync(DateTime at)
    {
        await using AsyncServiceScope scope = StandingIn(at);

        return await OutcomeService(scope)
            .RecordAsync(Cancel)
            .WaitAsync(TimeSpan.FromSeconds(60), Cancel);
    }

    private async Task AddAsync(Reservation reservation, ReservationOutcomeKind kind, DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Outcomes(scope).AddAsync(
            ReservationOutcome.Record(ReservationOutcomeId.New(), reservation, kind, null, null, [], [], at),
            Cancel);
    }

    private async Task<IReadOnlyList<ReservationOutcome>> ForAsync(ReservationId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Outcomes(scope).ListForReservationAsync(id, Cancel);
    }

    private async Task<ReservationState> StateOfAsync(ReservationId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return (await Reservations(scope).FindAsync(id, Cancel))!.State;
    }

    private async Task<ReservationStanding> StandingOfAsync(ReservationId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return (await Reservations(scope).FindAsync(id, Cancel))!.Standing;
    }

    private async Task WritingAsync(Reservation reservation, DateTime from)
    {
        var id = RecordingId.New();

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Recordings(scope).AddAsync(
            Recording.Begin(
                id,
                reservation.Id,
                reservation.Programme,
                new OutputRoot("primary"),
                RecordingFileName.For(id, ".ts"),
                from,
                reservation.EffectiveEndAt,
                new ProgrammeSnapshot(
                    reservation.SnapshotName,
                    string.Empty,
                    string.Empty,
                    [],
                    from,
                    AudioMode.Undetermined,
                    ProgrammeSnapshot.SoundsUnannounced),
                null,
                BroadcastGroupRole.Standalone,
                from,
                new TunerDeviceId("adapter0")),
            Cancel);
    }

    private async Task<Recording> EndedWholeAsync(
        Reservation reservation,
        DateTime from,
        params RecordingFault[] faults)
    {
        await WritingAsync(reservation, from);

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        IRecordingRepository recordings = Recordings(scope);
        Recording recording = Assert.Single(await recordings.ListForReservationAsync(reservation.Id, Cancel));

        foreach (RecordingFault fault in faults)
        {
            recording.Note(new OutcomeDetail(fault, null, string.Empty, reservation.EffectiveEndAt));
        }

        recording.Abort(reservation.EffectiveEndAt);
        recording.Settle(RecordingOutcome.Complete, 1_200_000, reservation.EffectiveEndAt);
        await recordings.SaveAsync(recording, Cancel);

        return recording;
    }

    private async Task<Reservation> LaidDownAsync(
        ReservationState state,
        DateTime startAt,
        DateTime endAt,
        DateTime? claimedAt = null,
        RecordingOutcome? outcome = null)
    {
        Reservation held = ReservationFixtures.Rehydrated(
            state,
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), startsAt: startAt),
            startAt: startAt,
            endAt: endAt);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Reservations(writing).AddAsync(held, Cancel);
        }

        if (claimedAt is { } moment)
        {
            await RunAsync(
                $"UPDATE reservation SET started_at = timestamptz '{moment:yyyy-MM-dd HH:mm:ss}+00' "
                + $"WHERE id = '{held.Id.Value}'");
        }

        if (outcome is { } written)
        {
            await RunAsync(
                $"UPDATE reservation SET recording_outcome = '{written}' WHERE id = '{held.Id.Value}'");
        }

        return held;
    }

    private async Task RunAsync(string sql)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await using var connection = new NpgsqlConnection(Context(scope).Database.GetConnectionString());
        await connection.OpenAsync(Cancel);

        await using var running = new NpgsqlCommand(sql, connection);
        await running.ExecuteNonQueryAsync(Cancel);
    }

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static IRecordingRepository Recordings(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRecordingRepository>();

    private static IReservationOutcomeRepository Outcomes(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationOutcomeRepository>();

    private static IReservationRepository Reservations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRepository>();

    private sealed class MeddlingOutcomes(
        IReservationOutcomeRepository outcomes,
        ReservationId meddledWith,
        Func<Task> meddle) : IReservationOutcomeRepository
    {
        private bool meddled;

        public async Task AddAsync(ReservationOutcome outcome, CancellationToken cancellationToken)
        {
            await outcomes.AddAsync(outcome, cancellationToken);

            if (!meddled && outcome.ReservationId.Equals(meddledWith))
            {
                meddled = true;
                await meddle();
            }
        }

        public Task<IReadOnlyList<ReservationOutcome>> ListAsync(OutcomeSpan span, CancellationToken cancellationToken)
            => outcomes.ListAsync(span, cancellationToken);

        public Task<PaginatedList<ReservationOutcome>> ListAsync(
            ReservationOutcomeQuery query,
            CancellationToken cancellationToken)
            => outcomes.ListAsync(query, cancellationToken);

        public Task<IReadOnlyList<ReservationOutcome>> ListForReservationAsync(
            ReservationId reservationId,
            CancellationToken cancellationToken)
            => outcomes.ListForReservationAsync(reservationId, cancellationToken);
    }
}
