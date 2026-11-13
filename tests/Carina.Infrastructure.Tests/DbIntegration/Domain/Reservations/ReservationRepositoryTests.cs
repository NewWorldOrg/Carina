using Carina.Domain.Base;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Configurations;
using Carina.Infrastructure.Tests.Fixtures.Reservations;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Npgsql;

namespace Carina.Infrastructure.Tests.DbIntegration.Domain.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationRepositoryTests(RepositoryDatabase database, MigratedRepositoryDatabase migratedDatabase)
    : IClassFixture<MigratedRepositoryDatabase>, IAsyncLifetime
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private static readonly ReservationWindow Everything = new(Now, Now.AddDays(14));

    private const int Network = 32736;

    private static readonly StandingCase[] Reachable =
    [
        new(ReservationStanding.Scheduled, ReservationState.Scheduled, false, null),
        new(ReservationStanding.Conflict, ReservationState.Conflict, false, null),
        new(ReservationStanding.Cancelled, ReservationState.Cancelled, false, null),
        new(ReservationStanding.Missed, ReservationState.Missed, false, null),
        new(ReservationStanding.Recording, ReservationState.Scheduled, true, null),
        new(ReservationStanding.Complete, ReservationState.Scheduled, true, RecordingOutcome.Complete),
        new(ReservationStanding.Truncated, ReservationState.Scheduled, true, RecordingOutcome.Truncated),
        new(ReservationStanding.Failed, ReservationState.Scheduled, true, RecordingOutcome.Failed),
    ];

    private static readonly DateTime Tick = new(2026, 8, 24, 20, 30, 0, DateTimeKind.Utc);

    private static readonly DateTime Made = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private static readonly ReservationState[] States =
    [
        ReservationState.Scheduled,
        ReservationState.Conflict,
        ReservationState.Cancelled,
        ReservationState.Missed,
    ];

    private static readonly (bool Claimed, string? Outcome)[] Claims =
    [
        (false, null),
        (true, null),
        (true, "Complete"),
        (true, "Failed"),
    ];

    private static readonly TimeSpan[] AirsForTheMatrix =
    [
        TimeSpan.FromHours(-2),
        TimeSpan.FromMinutes(-10),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(40),
        TimeSpan.FromHours(4),
    ];

    private static readonly int[] Margins = [0, 30];

    public static TheoryData<ReservationStanding, ReservationState, bool, RecordingOutcome?> EachOfThem
    {
        get
        {
            var data = new TheoryData<ReservationStanding, ReservationState, bool, RecordingOutcome?>();

            foreach (StandingCase reachable in Reachable)
            {
                data.Add(reachable.Standing, reachable.State, reachable.Claimed, reachable.Outcome);
            }

            return data;
        }
    }

    private readonly ServiceProvider container = database.Infrastructure();

    private readonly ServiceProvider migrated = migratedDatabase.Infrastructure();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        await container.DisposeAsync();
        await migrated.DisposeAsync();
    }

    [Fact]
    public async Task AReservationComesBackWithTheWindowAndTheSnapshotItWasStoredUnder()
    {
        Reservation planned = ReservationFixtures.Planned(
            marginBefore: Margin.OfSeconds(10),
            marginAfter: Margin.OfSeconds(30));
        await AddAsync(planned);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Reservation? found = await Reservations(scope).FindAsync(planned.Id, Cancel);

        Assert.NotNull(found);
        Assert.Equal(planned.StartAt, found.StartAt);
        Assert.Equal(planned.EndAt, found.EndAt);
        Assert.Equal(planned.EffectiveStartAt, found.EffectiveStartAt);
        Assert.Equal(planned.EffectiveEndAt, found.EffectiveEndAt);
        Assert.Equal("A programme", found.SnapshotName);
        Assert.Equal(ReservationState.Scheduled, found.State);
        Assert.False(found.IsPinned);
    }

    [Fact]
    public async Task AProgrammeIsFoundByTheFourThingsThatNameIt()
    {
        Reservation planned = ReservationFixtures.Planned();
        await AddAsync(planned);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Reservation? found = await Reservations(scope)
            .FindByProgrammeAsync(planned.Programme, Cancel);

        Assert.NotNull(found);
        Assert.Equal(planned.Id, found.Id);
    }

    [Fact]
    public async Task AReservationStillAheadIsFoundForItsBroadcastAfterTheGuideMovedTheStart()
    {
        Reservation planned = ReservationFixtures.Planned();
        await AddAsync(planned);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Reservation? found = await Reservations(scope)
            .FindByProgrammeAsync(Later(planned, minutes: 2), Cancel);

        Assert.NotNull(found);
        Assert.Equal(planned.Id, found.Id);
    }

    [Fact]
    public async Task AReservationNoLongerAheadIsNotTakenForTheBroadcastAtAnotherStart()
    {
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation recorded = ReservationFixtures.Planned();
        await AddAsync(cancelled, recorded);
        await ClaimAsync(recorded.Id, Now);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReservationRepository repository = Reservations(scope);

        Assert.Null(await repository.FindByProgrammeAsync(
            Later(cancelled, minutes: 2),
            Cancel));
        Assert.Null(await repository.FindByProgrammeAsync(
            Later(recorded, minutes: 2),
            Cancel));
    }

    [Fact]
    public async Task AWriteFromAReadingAnotherHandHasSinceChangedIsRefusedRatherThanWrittenOver()
    {
        Reservation planned = ReservationFixtures.Planned();
        await AddAsync(planned);

        await using AsyncServiceScope stale = container.CreateAsyncScope();
        IReservationRepository staleRepository = Reservations(stale);
        Reservation held = (await staleRepository.FindAsync(planned.Id, Cancel))!;

        await using (AsyncServiceScope meanwhile = container.CreateAsyncScope())
        {
            IReservationRepository repository = Reservations(meanwhile);
            Reservation changed = (await repository.FindAsync(planned.Id, Cancel))!;
            changed.Reprioritise(new Priority(55));
            await repository.SaveAllAsync([changed], Cancel);
        }

        held.Rewish(false);

        await Assert.ThrowsAsync<ReservationMovedMeanwhileException>(
            () => staleRepository.SaveAllAsync([held], Cancel));

        Reservation? readAgain = await staleRepository.FindAsync(planned.Id, Cancel);

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Reservation? stored = await Reservations(reading).FindAsync(planned.Id, Cancel);

        Assert.Equal(new Priority(55), stored?.Priority);
        Assert.True(stored?.EncodeWhenRecorded);
        Assert.Equal(new Priority(55), readAgain?.Priority);
    }

    [Fact]
    public async Task WhatIsPendingLeavesOutWhatWasCancelledOrMissed()
    {
        Reservation standing = ReservationFixtures.Planned();
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation missed = ReservationFixtures.Rehydrated(ReservationState.Missed);
        Reservation contended = ReservationFixtures.Rehydrated(ReservationState.Conflict);
        await AddAsync(standing, cancelled, missed, contended);

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.Contains(standing.Id, pending);
        Assert.Contains(contended.Id, pending);
        Assert.DoesNotContain(cancelled.Id, pending);
        Assert.DoesNotContain(missed.Id, pending);
    }

    [Fact]
    public async Task ARecordingThatHasEndedStopsBeingPendingEvenThoughItsStateStillReadsScheduled()
    {
        Reservation finished = ReservationFixtures.Planned();
        await AddAsync(finished);
        await ClaimAsync(finished.Id, Now);
        await SettleAsync(finished.Id, RecordingOutcome.Complete);

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.DoesNotContain(finished.Id, pending);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Reservation? read = await Reservations(scope).FindAsync(finished.Id, Cancel);

        Assert.Equal(ReservationState.Scheduled, read!.State);
        Assert.Equal(RecordingOutcome.Complete, read.RecordingOutcome);
    }

    [Fact]
    public async Task ARecordingStillRunningIsPendingEvenWhenItsWindowIsBehindTheOneAskedFor()
    {
        Reservation running = ReservationFixtures.Planned(
            startAt: Now.AddHours(-9),
            endAt: Now.AddHours(-8));
        await AddAsync(running);
        await ClaimAsync(running.Id, Now.AddHours(-9));

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.Contains(running.Id, pending);
    }

    [Fact]
    public async Task AReservationWhollyOutsideTheWindowIsNotPending()
    {
        Reservation past = ReservationFixtures.Planned(
            startAt: Now.AddHours(-9),
            endAt: Now.AddHours(-8));
        Reservation ahead = ReservationFixtures.Planned(
            startAt: Now.AddDays(30),
            endAt: Now.AddDays(30).AddHours(1));
        await AddAsync(past, ahead);

        IReadOnlyList<ReservationId> pending = await PendingAsync(Everything);

        Assert.DoesNotContain(past.Id, pending);
        Assert.DoesNotContain(ahead.Id, pending);
    }

    [Fact]
    public async Task SavingThemAllCarriesEveryChangeToTheLedger()
    {
        Reservation first = ReservationFixtures.Planned();
        Reservation second = ReservationFixtures.Planned();
        await AddAsync(first, second);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IReservationRepository repository = Reservations(writing);
            Reservation one = (await repository.FindAsync(first.Id, Cancel))!;
            Reservation other = (await repository.FindAsync(second.Id, Cancel))!;
            one.Contend();
            other.LoseReception(Now);

            await repository.SaveAllAsync([one, other], Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        IReservationRepository read = Reservations(reading);

        Assert.Equal(ReservationState.Conflict, (await read.FindAsync(first.Id, Cancel))!.State);
        Assert.True((await read.FindAsync(second.Id, Cancel))!.ReceptionUnavailable);
    }

    [Fact]
    public async Task BrRd010AReservationMovedOntoAnotherListingComesBackOnThatListingUnderItsOwnIdentifier()
    {
        Reservation planned = ReservationFixtures.Planned();
        await AddAsync(planned);
        ProgrammeRef primary = ReservationFixtures.Programme(
            ReservationFixtures.NextEventId(),
            serviceId: 1032,
            startsAt: planned.ProgrammeStartsAt.AddMinutes(-5));
        BroadcastGroupKey key = new($"movement:{primary.Id}");

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IReservationRepository repository = Reservations(writing);
            Reservation loaded = (await repository.FindAsync(planned.Id, Cancel))!;
            loaded.Retarget(
                primary,
                primary.StartsAt.AddHours(1),
                true,
                ReservationFixtures.Snapshot("The primary listing"),
                [new EpgDivergence(DivergedField.Service, "32736-1024", "32736-1032", Now)],
                key,
                BroadcastGroupRole.MovementPrimary);

            await repository.SaveAllAsync([loaded], Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Reservation? found = await Reservations(reading).FindByProgrammeAsync(primary, Cancel);

        Assert.NotNull(found);
        Assert.Equal(planned.Id, found.Id);
        Assert.Equal("The primary listing", found.SnapshotName);
        Assert.Equal(key, found.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, found.BroadcastGroupRole);
        Assert.Equal(DivergedField.Service, Assert.Single(found.EpgDivergences).Field);
    }

    [Fact]
    public async Task BrRd010AReservationCancelledForTheSameBroadcastIsKeptWithThatReason()
    {
        Reservation planned = ReservationFixtures.Planned();
        await AddAsync(planned);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IReservationRepository repository = Reservations(writing);
            Reservation loaded = (await repository.FindAsync(planned.Id, Cancel))!;
            loaded.Cancel(ReservationCancellation.SameBroadcast);

            await repository.SaveAllAsync([loaded], Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Reservation? found = await Reservations(reading).FindAsync(planned.Id, Cancel);

        Assert.Equal(ReservationCancellation.SameBroadcast, found!.Cancellation);
    }

    [Fact]
    public async Task WithdrawingARuleBornReservationLeavesNoRowBehind()
    {
        Reservation withdrawn = ReservationFixtures.Planned();
        await AddAsync(withdrawn);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            IReservationRepository repository = Reservations(writing);
            Reservation loaded = (await repository.FindAsync(withdrawn.Id, Cancel))!;

            await repository.WithdrawAsync([loaded], Cancel);
        }

        await using AsyncServiceScope reading = container.CreateAsyncScope();

        Assert.Null(await Reservations(reading).FindAsync(withdrawn.Id, Cancel));
    }

    [Fact]
    public async Task AReservationThatWasCancelledLeavesTheTableAltogether()
    {
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation beside = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        await AddAsyncForReservationDiscard(cancelled, beside);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(cancelled.Id));

        Assert.Equal(0, await ReservationRows(cancelled.Id));
        Assert.Equal(1, await ReservationRows(beside.Id));
    }

    [Theory]
    [InlineData(ReservationState.Cancelled)]
    [InlineData(ReservationState.Missed)]
    public async Task AReservationNothingIsWaitingOnGoesWhileItsBroadcastIsStillAhead(ReservationState state)
    {
        Reservation standing = ReservationFixtures.Rehydrated(state);
        await AddAsyncForReservationDiscard(standing);

        Assert.True(standing.EffectiveEndAt > Now);
        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(standing.Id));
        Assert.Equal(0, await ReservationRows(standing.Id));
    }

    [Theory]
    [InlineData(ReservationState.Scheduled)]
    [InlineData(ReservationState.Conflict)]
    public async Task AReservationStillToBeRecordedIsRefusedRatherThanThrownAway(ReservationState state)
    {
        Reservation standing = ReservationFixtures.Rehydrated(state);
        await AddAsyncForReservationDiscard(standing);

        Assert.True(standing.EffectiveEndAt > Now);
        Assert.Equal(ReservationDiscard.StillToBeRecorded, await DiscardAsync(standing.Id));
        Assert.Equal(1, await ReservationRows(standing.Id));
    }

    [Theory]
    [InlineData(ReservationState.Scheduled)]
    [InlineData(ReservationState.Conflict)]
    public async Task AReservationNothingWillEverRecordNowGoesEvenThoughItStillStands(ReservationState state)
    {
        Reservation over = Closed(state);
        await AddAsyncForReservationDiscard(over);

        Assert.True(over.EffectiveEndAt <= Now);
        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(over.Id));
        Assert.Equal(0, await ReservationRows(over.Id));
    }

    [Fact]
    public async Task AReservationIsStillToBeRecordedRightUpToTheMomentItsWindowCloses()
    {
        Reservation standing = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(-1),
            marginAfter: Margin.OfSeconds(600));
        await AddAsyncForReservationDiscard(standing);

        Assert.Equal(
            ReservationDiscard.StillToBeRecorded,
            await DiscardAsync(standing.Id, standing.EffectiveEndAt.AddSeconds(-1)));
        Assert.Equal(1, await ReservationRows(standing.Id));
    }

    [Fact]
    public async Task AReservationGoesFromTheMomentItsWindowCloses()
    {
        Reservation standing = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(-1),
            marginAfter: Margin.OfSeconds(600));
        await AddAsyncForReservationDiscard(standing);

        Assert.Equal(
            ReservationDiscard.Discarded,
            await DiscardAsync(standing.Id, standing.EffectiveEndAt));
        Assert.Equal(0, await ReservationRows(standing.Id));
    }

    [Fact]
    public async Task AReservationGoesAfterItsWindowHasClosed()
    {
        Reservation standing = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(-1),
            marginAfter: Margin.OfSeconds(600));
        await AddAsyncForReservationDiscard(standing);

        Assert.Equal(
            ReservationDiscard.Discarded,
            await DiscardAsync(standing.Id, standing.EffectiveEndAt.AddSeconds(1)));
        Assert.Equal(0, await ReservationRows(standing.Id));
    }

    [Fact]
    public async Task TheMarginAfterTheBroadcastIsPartOfTheWindowThatIsRead()
    {
        DateTime ends = Now.AddHours(-1);
        Reservation without = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: ends);
        Reservation waiting = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: ends,
            marginAfter: Margin.OfSeconds(600));
        await AddAsyncForReservationDiscard(without, waiting);

        DateTime between = ends.AddSeconds(300);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(without.Id, between));
        Assert.Equal(ReservationDiscard.StillToBeRecorded, await DiscardAsync(waiting.Id, between));
    }

    [Fact]
    public async Task AReservationStoppedEarlyGoesOnceItsRecordingIsGoneThoughItsWindowIsOpen()
    {
        Reservation recorded = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsyncForReservationDiscard(recorded);
        await ClaimAsyncForReservationDiscard(recorded.Id);
        Recording written = await RecordedAsync(recorded, settled: true);

        await using (AsyncServiceScope throwing = container.CreateAsyncScope())
        {
            Assert.Equal(
                RecordingDiscard.Discarded,
                await Directories(throwing).DiscardAsync(written.Id, Cancel));
        }

        Assert.True(recorded.EffectiveEndAt > Now);
        Assert.Equal(RecordingOutcome.Complete, await OutcomeAsync(recorded.Id));
        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(recorded.Id));
    }

    [Fact]
    public async Task AMomentThatIsNotInUtcIsRefusedRatherThanComparedAgainstTheWindow()
    {
        Reservation standing = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        await AddAsyncForReservationDiscard(standing);

        await using AsyncServiceScope discarding = container.CreateAsyncScope();
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => Reservations(discarding).DiscardAsync(
                standing.Id,
                DateTime.SpecifyKind(Now, DateTimeKind.Local),
                Cancel));

        Assert.Equal("at", refused.ParamName);
        Assert.Equal(1, await ReservationRows(standing.Id));
    }

    [Fact]
    public async Task AReservationARecordingCameOfIsRefusedUntilThatRecordingIsThrownAway()
    {
        Reservation recorded = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsyncForReservationDiscard(recorded);
        await ClaimAsyncForReservationDiscard(recorded.Id);
        Recording written = await RecordedAsync(recorded, settled: true);

        Assert.Equal(ReservationDiscard.RecordingCameOfIt, await DiscardAsync(recorded.Id));
        Assert.Equal(1, await ReservationRows(recorded.Id));
        Assert.Equal(RecordingOutcome.Complete, await OutcomeAsync(recorded.Id));

        await using (AsyncServiceScope throwing = container.CreateAsyncScope())
        {
            Assert.Equal(
                RecordingDiscard.Discarded,
                await Directories(throwing).DiscardAsync(written.Id, Cancel));
        }

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(recorded.Id));
        Assert.Equal(0, await ReservationRows(recorded.Id));
    }

    [Fact]
    public async Task AReservationBeingWrittenRightNowIsRefusedBecauseItsRecordingIsThere()
    {
        Reservation writing = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsyncForReservationDiscard(writing);
        await ClaimAsyncForReservationDiscard(writing.Id);
        Recording begun = await RecordedAsync(writing, settled: false);

        Assert.Equal(ReservationDiscard.RecordingCameOfIt, await DiscardAsync(writing.Id));
        Assert.Equal(1, await ReservationRows(writing.Id));
        Assert.Equal(1, await RecordingRows(begun.Id));
    }

    [Fact]
    public async Task AReservationTakenUpBeforeItsRecordingIsWrittenDownIsRefused()
    {
        Reservation claimed = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsyncForReservationDiscard(claimed);

        await ClaimAsyncForReservationDiscard(claimed.Id);

        Assert.Equal(ReservationDiscard.TurningIntoARecording, await DiscardAsync(claimed.Id));
        Assert.Equal(1, await ReservationRows(claimed.Id));
    }

    [Fact]
    public async Task AReservationNothingEverWroteDownIsSaidToBeMissingRatherThanRefused()
    {
        Assert.Equal(ReservationDiscard.NoSuchReservation, await DiscardAsync(ReservationId.New()));
    }

    [Fact]
    public async Task ThrowingTheSameReservationAwayTwiceSaysItIsGoneTheSecondTime()
    {
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        await AddAsyncForReservationDiscard(cancelled);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(cancelled.Id));
        Assert.Equal(ReservationDiscard.NoSuchReservation, await DiscardAsync(cancelled.Id));
    }

    [Fact]
    public async Task ThrowingAReservationAwayLeavesTheRecordingsOfEveryOtherReservationWhereTheyAre()
    {
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation recorded = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsyncForReservationDiscard(cancelled, recorded);
        await ClaimAsyncForReservationDiscard(recorded.Id);
        Recording beside = await RecordedAsync(recorded, settled: true);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(cancelled.Id));

        Assert.Equal(1, await RecordingRows(beside.Id));
        Assert.Equal(1, await ReservationRows(recorded.Id));
    }

    [Fact]
    public async Task AReservationThatLostItsContestIsCountedAndOneThatIsSecuredIsNot()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation lost = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        lost.Contend();
        await AddAsync(lost, ReservationFixtures.Planned(startAt: Now.AddHours(2)));

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.Contended - before.Contended);
        Assert.Equal(Now, after.AsOf);
    }

    [Fact]
    public async Task AContestAlreadyDecidedByTheClockIsHistoryRatherThanHealth()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation over = ReservationFixtures.Planned(startAt: Now.AddHours(-3), endAt: Now.AddHours(-2));
        over.Contend();
        Reservation heldOpenByItsMargin = ReservationFixtures.Planned(
            startAt: Now.AddHours(-2),
            endAt: Now.AddSeconds(-10),
            marginAfter: Margin.OfSeconds(30));
        heldOpenByItsMargin.Contend();
        await AddAsync(over, heldOpenByItsMargin);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.Contended - before.Contended);
    }

    [Fact]
    public async Task WhatIsCancelledMissedOrAlreadyRecordedStandsInNobodysWay()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation cancelled = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        cancelled.Contend();
        cancelled.Cancel(ReservationCancellation.ByHand);
        Reservation missed = ReservationFixtures.Rehydrated(ReservationState.Missed, startAt: Now.AddHours(2));
        Reservation recorded = ReservationFixtures.Rehydrated(
            ReservationState.Scheduled,
            startAt: Now.AddHours(-2),
            endAt: Now.AddHours(2),
            receptionUnavailable: true,
            receptionUnavailableSince: Now);
        await AddAsync(cancelled, missed, recorded);
        await RecordedAsyncForReservationHealth(recorded.Id, Now.AddHours(-2), RecordingOutcome.Complete);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(before.Contended, after.Contended);
        Assert.Equal(before.ReceptionUnavailable, after.ReceptionUnavailable);
        Assert.Equal(before.EpgDiverged, after.EpgDiverged);
        Assert.Equal(before.EpgMissing, after.EpgMissing);
    }

    [Fact]
    public async Task AReservationWithNowhereToTuneIsCounted()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation nowhere = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        nowhere.LoseReception(Now);
        await AddAsync(nowhere);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.ReceptionUnavailable - before.ReceptionUnavailable);
        Assert.Equal(before.Contended, after.Contended);
    }

    [Fact]
    public async Task ADivergenceOrAVanishedProgrammeIsCountedUntilSomebodyAcknowledgesIt()
    {
        ReservationHealth before = await HealthAsync(Now);

        Reservation moved = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        moved.Diverge([new EpgDivergence(DivergedField.StartAt, "12:00", "12:05", Now)]);
        Reservation seen = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        seen.Diverge([new EpgDivergence(DivergedField.Name, "Before", "After", Now)]);
        seen.Acknowledge(Now);
        Reservation vanished = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        vanished.Disappear();
        Reservation vanishedAndSeen = ReservationFixtures.Planned(startAt: Now.AddHours(2));
        vanishedAndSeen.Disappear();
        vanishedAndSeen.Acknowledge(Now);
        await AddAsync(moved, seen, vanished, vanishedAndSeen);

        ReservationHealth after = await HealthAsync(Now);

        Assert.Equal(1, after.EpgDiverged - before.EpgDiverged);
        Assert.Equal(1, after.EpgMissing - before.EpgMissing);
    }

    [Fact]
    public async Task TheMomentIsAUtcInstantOrTheCountIsRefused()
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        await Assert.ThrowsAsync<ArgumentException>(() => Reservations(scope).HealthAsync(
            new DateTime(2026, 8, 24, 12, 0, 0, DateTimeKind.Local),
            Cancel));
    }

    [Fact]
    public async Task ThePageSaysHowManyThereAreAndHandsBackOnlyTheOnesOnIt()
    {
        const int Service = 1101;
        await AddAsync(
            Planned(Service, Now.AddHours(2)),
            Planned(Service, Now.AddHours(4)),
            Planned(Service, Now.AddHours(6)));

        PaginatedList<Reservation> page = await ListAsync(
            Only(Service) with { },
            perPage: 2);

        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.LastPage);
        Assert.Equal(1, page.CurrentPage);
        Assert.Equal(2, page.PerPage);
    }

    [Fact]
    public async Task TheSecondPageCarriesOnWhereTheFirstStopped()
    {
        const int Service = 1102;
        await AddAsync(
            Planned(Service, Now.AddHours(2)),
            Planned(Service, Now.AddHours(4)),
            Planned(Service, Now.AddHours(6)));

        PaginatedList<Reservation> first = await ListAsync(Only(Service), perPage: 2);
        PaginatedList<Reservation> second = await ListAsync(Only(Service), perPage: 2, page: 2);

        Assert.Single(second.Items);
        Assert.Empty(first.Items.Select(item => item.Id).Intersect(second.Items.Select(item => item.Id)));
        Assert.Equal(Now.AddHours(6), second.Items[0].StartAt);
    }

    [Fact]
    public async Task OnlyTheChannelsAskedForComeBack()
    {
        const int Wanted = 1103;
        const int Beside = 1104;
        await AddAsync(Planned(Wanted, Now.AddHours(2)), Planned(Beside, Now.AddHours(2)));

        PaginatedList<Reservation> page = await ListAsync(Only(Wanted));

        Assert.Single(page.Items);
        Assert.Equal(Wanted, page.Items[0].ServiceId.Value);
    }

    [Fact]
    public async Task MoreThanOneChannelIsAskedForAtOnce()
    {
        const int First = 1105;
        const int Second = 1106;
        const int Beside = 1107;
        await AddAsync(
            Planned(First, Now.AddHours(2)),
            Planned(Second, Now.AddHours(2)),
            Planned(Beside, Now.AddHours(2)));

        PaginatedList<Reservation> page = await ListAsync(new ReservationConditions
        {
            Channels = [new ProgrammeService(Network, First), new ProgrammeService(Network, Second)],
        });

        Assert.Equal(2, page.Total);
        Assert.Equal([First, Second], page.Items.Select(item => item.ServiceId.Value).Order());
    }

    [Fact]
    public async Task AReservationARuleMadeAndOneAHandMadeAreToldApart()
    {
        const int Service = 1108;
        RuleId rule = await DraftedAsync("Drama");
        await AddAsync(Planned(Service, Now.AddHours(2)), Planned(Service, Now.AddHours(4), rule: rule));

        PaginatedList<Reservation> byHand = await ListAsync(
            Only(Service) with { Origin = ReservationOrigin.ByHand });
        PaginatedList<Reservation> byRule = await ListAsync(
            Only(Service) with { Origin = ReservationOrigin.ByRule });

        Assert.Single(byHand.Items);
        Assert.False(byHand.Items[0].IsRuleBorn);
        Assert.Single(byRule.Items);
        Assert.True(byRule.Items[0].IsRuleBorn);
    }

    [Fact]
    public async Task AKeywordReadsWhatTheReservationKeptOfTheProgramme()
    {
        const int Service = 1109;
        await AddAsync(
            Planned(Service, Now.AddHours(2), snapshot: ReservationFixtures.Snapshot("Harbour report", "The tide")),
            Planned(Service, Now.AddHours(4), snapshot: ReservationFixtures.Snapshot("Kitchen notes", "Vegetables")));

        PaginatedList<Reservation> byName = await ListAsync(Only(Service) with { Keyword = "harbour" });
        PaginatedList<Reservation> bySummary = await ListAsync(Only(Service) with { Keyword = "vegetables" });
        PaginatedList<Reservation> byNeither = await ListAsync(Only(Service) with { Keyword = "orchestra" });

        Assert.Equal("Harbour report", Assert.Single(byName.Items).SnapshotName);
        Assert.Equal("Kitchen notes", Assert.Single(bySummary.Items).SnapshotName);
        Assert.Empty(byNeither.Items);
    }

    [Fact]
    public async Task AKeywordDoesNotCareWhichCaseItWasTypedIn()
    {
        const int Service = 1110;
        await AddAsync(
            Planned(Service, Now.AddHours(2), snapshot: ReservationFixtures.Snapshot("Harbour report", "The tide")));

        Assert.Single((await ListAsync(Only(Service) with { Keyword = "HARBOUR" })).Items);
        Assert.Single((await ListAsync(Only(Service) with { Keyword = "harbour" })).Items);
    }

    [Fact]
    public async Task ASpanNarrowsTheListToWhatStartsInsideIt()
    {
        const int Service = 1111;
        await AddAsync(
            Planned(Service, Now.AddHours(2)),
            Planned(Service, Now.AddHours(8)),
            Planned(Service, Now.AddHours(20)));

        PaginatedList<Reservation> page = await ListAsync(
            Only(Service),
            from: Now.AddHours(4),
            to: Now.AddHours(12));

        Assert.Equal(Now.AddHours(8), Assert.Single(page.Items).StartAt);
    }

    [Fact]
    public async Task TheStartOfASpanIsInsideItAndTheEndIsNot()
    {
        const int Service = 1112;
        await AddAsync(Planned(Service, Now.AddHours(4)), Planned(Service, Now.AddHours(12)));

        PaginatedList<Reservation> page = await ListAsync(
            Only(Service),
            from: Now.AddHours(4),
            to: Now.AddHours(12));

        Assert.Equal(Now.AddHours(4), Assert.Single(page.Items).StartAt);
    }

    [Fact]
    public async Task TheListIsSortedTheWayItWasAskedFor()
    {
        const int Service = 1113;
        await AddAsync(
            Planned(Service, Now.AddHours(2), priority: new Priority(10)),
            Planned(Service, Now.AddHours(4), priority: new Priority(50)),
            Planned(Service, Now.AddHours(6), priority: new Priority(30)));

        PaginatedList<Reservation> byStart = await ListAsync(Only(Service));
        PaginatedList<Reservation> byStartBackwards = await ListAsync(Only(Service), descending: true);
        PaginatedList<Reservation> byPriority = await ListAsync(Only(Service), sort: ReservationSort.Priority);
        PaginatedList<Reservation> byPriorityBackwards = await ListAsync(
            Only(Service),
            sort: ReservationSort.Priority,
            descending: true);

        Assert.Equal(Now.AddHours(2), byStart.Items[0].StartAt);
        Assert.Equal(Now.AddHours(6), byStartBackwards.Items[0].StartAt);
        Assert.Equal([10, 30, 50], byPriority.Items.Select(item => item.Priority.Value));
        Assert.Equal([50, 30, 10], byPriorityBackwards.Items.Select(item => item.Priority.Value));
    }

    [Fact]
    public async Task EveryReservationOnTheChannelComesBackWhenNothingNarrowsTheList()
    {
        const int Service = 1114;
        Reservation standing = Planned(Service, Now.AddHours(2));
        Reservation cancelled = Planned(Service, Now.AddHours(4));
        await AddAsync(standing, cancelled);
        await CancelAsync(cancelled.Id);

        PaginatedList<Reservation> page = await ListAsync(Only(Service));

        Assert.Equal([standing.Id, cancelled.Id], page.Items.Select(item => item.Id));
    }

    [Theory]
    [MemberData(nameof(EachOfThem))]
    public async Task WhatAReservationSaysItStandsAsIsWhatTheLedgerWorkedOutForIt(
        ReservationStanding standing,
        ReservationState state,
        bool claimed,
        RecordingOutcome? outcome)
    {
        Reservation held = await LaidDownAsync(state, claimed, outcome);

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Reservation found = (await Reservations(scope).FindAsync(held.Id, Cancel))!;

        Assert.Equal(standing, found.Standing);
        Assert.Equal(standing.ToString(), await ComputedAsync(Context(scope), found));
    }

    [Fact]
    public void EveryStandingAReservationCanShowIsOneTheLedgerCanBeMadeToShow()
    {
        Assert.Equal(
            [.. Enum.GetValues<ReservationStanding>().Order()],
            [.. Reachable.Select(reachable => reachable.Standing).Distinct().Order()]);
    }

    [Fact]
    public async Task TheStandingAskedForIsTheOnlyOneTheListHandsBack()
    {
        var laid = new List<(ReservationStanding Standing, ReservationId Id)>();

        foreach (StandingCase reachable in Reachable)
        {
            Reservation held = await LaidDownAsync(reachable.State, reachable.Claimed, reachable.Outcome);

            laid.Add((reachable.Standing, held.Id));
        }

        foreach ((ReservationStanding standing, ReservationId id) in laid)
        {
            await using AsyncServiceScope scope = container.CreateAsyncScope();
            ReservationQuery query = ReservationQuery.For(
                null,
                null,
                perPage: ReservationQuery.MostPerPage,
                conditions: new ReservationConditions { Standings = [standing] })!;

            IReadOnlyList<Reservation> found =
                (await Reservations(scope).ListAsync(query, Cancel)).Items;

            Assert.Contains(id, found.Select(reservation => reservation.Id));
            Assert.All(found, reservation => Assert.Equal(standing, reservation.Standing));
        }
    }

    [Fact(DisplayName = "BR-RS-001: the reservations the store lists as pending are the ones each reservation says is pending")]
    public async Task TheReservationsListedAsPendingAreTheOnesEachSaysIsPending()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();
        ReservationWindow[] windows =
        [
            new(Tick - Margin.Longest, DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc)),
            new(Tick, Tick.AddHours(1)),
            new(Tick.AddHours(-3), Tick.AddHours(-2)),
        ];

        foreach (ReservationWindow window in windows)
        {
            await using AsyncServiceScope scope = migrated.CreateAsyncScope();
            IReadOnlyList<Reservation> pending =
                await Reservations(scope).ListPendingAsync(window, CancellationToken.None);

            Assert.Equal(
                Ids(matrix.Where(reservation => reservation.IsPendingWithin(window))),
                Ids(pending.Select(reservation => reservation.Id)));
        }
    }

    [Fact(DisplayName = "BR-RS-001: on the instant a reservation starts or ends, the store lists as pending what each reservation says is pending")]
    public async Task OnTheEdgesOfEveryWindowTheStoreListsWhatEachSaysIsPending()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();
        DateTime earliest = Tick.AddDays(-1);
        DateTime latest = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        ReservationWindow[] windows =
        [
            .. matrix
                .SelectMany(reservation => new ReservationWindow[]
                {
                    new(reservation.EndAt, latest),
                    new(reservation.EndAt.AddSeconds(1), latest),
                    new(earliest, reservation.StartAt),
                    new(earliest, reservation.StartAt.AddSeconds(-1)),
                })
                .Distinct(),
        ];

        foreach (ReservationWindow window in windows)
        {
            await using AsyncServiceScope scope = migrated.CreateAsyncScope();
            IReadOnlyList<Reservation> pending =
                await Reservations(scope).ListPendingAsync(window, CancellationToken.None);

            Assert.Equal(
                Ids(matrix.Where(reservation => reservation.IsPendingWithin(window))),
                Ids(pending.Select(reservation => reservation.Id)));
        }
    }

    [Fact(DisplayName = "BR-RS-001: a second before a reservation's window closes and on the instant it closes, the store throws it away as the reservation says")]
    public async Task OnTheCloseOfEveryWindowTheStoreThrowsAwayAsEachSays()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach (Reservation reservation in matrix)
        {
            DateTime before = reservation.EffectiveEndAt.AddSeconds(-1);

            if (reservation.Discarding(false, before) is not ReservationDiscard.Discarded)
            {
                Assert.Equal(reservation.Discarding(false, before), await DiscardAsyncForTheMatrix(reservation.Id, before));
            }

            Assert.Equal(
                reservation.Discarding(false, reservation.EffectiveEndAt),
                await DiscardAsyncForTheMatrix(reservation.Id, reservation.EffectiveEndAt));
        }
    }

    [Fact(DisplayName = "BR-RS-001: what the store does with a reservation asked to be thrown away is what the reservation says it comes to")]
    public async Task WhatTheStoreDoesWithAReservationThrownAwayIsWhatItSaysItComesTo()
    {
        IReadOnlyList<Reservation> matrix = await LaidAsync();

        foreach (Reservation reservation in matrix)
        {
            await using AsyncServiceScope scope = migrated.CreateAsyncScope();

            Assert.Equal(
                reservation.Discarding(false, Tick),
                await Reservations(scope).DiscardAsync(reservation.Id, Tick, CancellationToken.None));
        }
    }

    private static async Task<NpgsqlConnection> OpenAsync(CarinaDbContext context)
    {
        var connection = new NpgsqlConnection(context.Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }

    private async Task<IReadOnlyList<ReservationId>> PendingAsync(ReservationWindow window)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return
        [
            .. (await Reservations(scope).ListPendingAsync(window, Cancel))
                .Select(reservation => reservation.Id),
        ];
    }

    private async Task AddAsync(params Reservation[] reservations)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReservationRepository repository = Reservations(scope);

        foreach (Reservation reservation in reservations)
        {
            await repository.AddAsync(reservation, Cancel);
        }
    }

    private async Task ClaimAsync(ReservationId id, DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        Assert.True(await Contract(scope).ClaimAsync(id, at, Cancel));
    }

    private async Task SettleAsync(ReservationId id, RecordingOutcome outcome)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await using NpgsqlConnection connection = await OpenAsync(Context(scope));
        await using var settling = new NpgsqlCommand(
            $"UPDATE reservation SET recording_outcome = '{outcome}' WHERE id = '{id.Value}'",
            connection);

        await settling.ExecuteNonQueryAsync(Cancel);
    }

    private static ProgrammeRef Later(Reservation reservation, int minutes)
        => new(
            reservation.NetworkId,
            reservation.ServiceId,
            reservation.EventId,
            reservation.ProgrammeStartsAt.AddMinutes(minutes));

    private static CarinaDbContext Context(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<CarinaDbContext>();

    private static IReservationRecordingContract Contract(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRecordingContract>();

    private static IReservationRepository Reservations(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IReservationRepository>();

    private static Reservation Closed(ReservationState state)
        => ReservationFixtures.Rehydrated(state, startAt: Now.AddHours(-3), endAt: Now.AddHours(-2));

    private async Task ClaimAsyncForReservationDiscard(ReservationId id)
    {
        await using AsyncServiceScope claiming = container.CreateAsyncScope();

        Assert.True(await Contract(claiming).ClaimAsync(id, Now, Cancel));
    }

    private async Task<ReservationDiscard> DiscardAsync(ReservationId id, DateTime? at = null)
    {
        await using AsyncServiceScope discarding = container.CreateAsyncScope();

        return await Reservations(discarding).DiscardAsync(id, at ?? Now, Cancel);
    }

    private async Task<int> ReservationRows(ReservationId id)
    {
        await using AsyncServiceScope counting = container.CreateAsyncScope();

        return await Context(counting).Set<Reservation>().CountAsync(reservation => reservation.Id == id, Cancel);
    }

    private async Task<int> RecordingRows(RecordingId id)
    {
        await using AsyncServiceScope counting = container.CreateAsyncScope();

        return await Context(counting).Set<Recording>().CountAsync(recording => recording.Id == id, Cancel);
    }

    private async Task<RecordingOutcome?> OutcomeAsync(ReservationId id)
    {
        await using AsyncServiceScope reading = container.CreateAsyncScope();
        Reservation? found = await Reservations(reading).FindAsync(id, Cancel);

        return found?.RecordingOutcome;
    }

    private async Task AddAsyncForReservationDiscard(params Reservation[] reservations)
    {
        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IReservationRepository repository = Reservations(writing);

        foreach (Reservation reservation in reservations)
        {
            await repository.AddAsync(reservation, Cancel);
        }
    }

    private async Task<Recording> RecordedAsync(Reservation reservation, bool settled)
    {
        RecordingId id = RecordingId.New();
        Recording begun = Recording.Begin(
            id,
            reservation.Id,
            reservation.Programme,
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            reservation.EffectiveStartAt,
            reservation.EffectiveEndAt,
            new ProgrammeSnapshot(
                "A programme",
                string.Empty,
                string.Empty,
                [],
                Now,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Now,
            new TunerDeviceId("adapter0"));

        await using AsyncServiceScope writing = container.CreateAsyncScope();
        IRecordingRepository repository = Recordings(writing);
        await repository.AddAsync(begun, Cancel);

        if (settled)
        {
            begun.Abort(reservation.EffectiveEndAt);
            begun.Settle(RecordingOutcome.Complete, 1_000_000, reservation.EffectiveEndAt);
            await repository.SaveAsync(begun, Cancel);
        }

        return begun;
    }

    private static IRecordingDirectory Directories(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRecordingDirectory>();

    private static IRecordingRepository Recordings(AsyncServiceScope scope)
        => scope.ServiceProvider.GetRequiredService<IRecordingRepository>();

    private async Task<ReservationHealth> HealthAsync(DateTime at)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Reservations(scope).HealthAsync(at, Cancel);
    }

    private async Task RecordedAsyncForReservationHealth(ReservationId id, DateTime claimedAt, RecordingOutcome outcome)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        await using var connection = new NpgsqlConnection(Context(scope).Database.GetConnectionString());
        await connection.OpenAsync(Cancel);

        await using var writing = new NpgsqlCommand(
            $"UPDATE reservation SET started_at = timestamptz '{claimedAt:yyyy-MM-dd HH:mm:ss}+00', "
            + $"recording_outcome = '{outcome}' WHERE id = '{id.Value}'",
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static ReservationConditions Only(int serviceId)
        => new() { Channels = [new ProgrammeService(Network, serviceId)] };

    private static Reservation Planned(
        int serviceId,
        DateTime startsAt,
        Priority? priority = null,
        RuleId? rule = null,
        ProgrammeSnapshot? snapshot = null)
        => ReservationFixtures.Planned(
            programme: ReservationFixtures.Programme(ReservationFixtures.NextEventId(), serviceId, startsAt),
            priority: priority,
            ruleId: rule,
            startAt: startsAt,
            endAt: startsAt.AddHours(1),
            snapshot: snapshot);

    private async Task<PaginatedList<Reservation>> ListAsync(
        ReservationConditions conditions,
        DateTime? from = null,
        DateTime? to = null,
        ReservationSort sort = ReservationSort.StartAt,
        bool descending = false,
        int? page = null,
        int? perPage = null)
    {
        ReservationQuery query = ReservationQuery.For(from, to, sort, descending, page, perPage, conditions)!;

        await using AsyncServiceScope scope = container.CreateAsyncScope();

        return await Reservations(scope).ListAsync(query, Cancel);
    }

    private async Task<RuleId> DraftedAsync(string name)
    {
        RuleId id = RuleId.New();

        await using AsyncServiceScope scope = container.CreateAsyncScope();
        Context(scope).Add(Rule.Draft(
            id,
            name,
            new RuleQuery("keyword=drama"),
            Priority.Default,
            true,
            Margin.None,
            Margin.None,
            Now));

        await Context(scope).SaveChangesAsync(Cancel);

        return id;
    }

    private async Task CancelAsync(ReservationId id)
    {
        await using AsyncServiceScope scope = container.CreateAsyncScope();
        IReservationRepository repository = Reservations(scope);
        Reservation found = (await repository.FindAsync(id, Cancel))!;

        found.Cancel(ReservationCancellation.ByHand);

        await repository.SaveAsync(found, Cancel);
    }

    private static async Task<string> ComputedAsync(CarinaDbContext context, Reservation found)
        => await context.Set<Reservation>()
            .Where(reservation => reservation.Id == found.Id)
            .Select(reservation => EF.Property<string>(reservation, ReservationConfiguration.CompositeState))
            .SingleAsync(Cancel);

    private async Task<Reservation> LaidDownAsync(
        ReservationState state,
        bool claimed,
        RecordingOutcome? outcome)
    {
        Reservation held = ReservationFixtures.Rehydrated(state);

        await using (AsyncServiceScope writing = container.CreateAsyncScope())
        {
            await Reservations(writing).AddAsync(held, Cancel);
        }

        if (claimed)
        {
            await RunAsync(
                $"UPDATE reservation SET started_at = timestamptz '2026-08-24 12:00:00+00' WHERE id = '{held.Id.Value}'");
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

    private async Task<ReservationDiscard> DiscardAsyncForTheMatrix(ReservationId id, DateTime at)
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Reservations(scope).DiscardAsync(id, at, CancellationToken.None);
    }

    private static IReadOnlyList<Guid> Ids(IEnumerable<Reservation> reservations)
        => Ids(reservations.Select(reservation => reservation.Id));

    private static IReadOnlyList<Guid> Ids(IEnumerable<ReservationId> ids) => [.. ids.Select(id => id.Value).Order()];

    /// <summary>
    /// Lays the whole matrix down as rows written straight into the store, then reads every row back as the
    /// reservation it is, so both arms judge the same row.
    /// </summary>
    private async Task<IReadOnlyList<Reservation>> LaidAsync()
    {
        await ExecuteAsync("DELETE FROM reservation");

        int eventId = 30000;

        foreach (Row row in Matrix())
        {
            await LayAsync(++eventId, row.State, Tick + row.Airs, row.Claimed, row.Outcome, row.Margin);
        }

        await using AsyncServiceScope scope = migrated.CreateAsyncScope();

        return await Context(scope).Set<Reservation>().AsNoTracking().ToListAsync();
    }

    private static IEnumerable<Row> Matrix()
        => from state in States
           from claim in Claims
           from airs in AirsForTheMatrix
           from margin in Margins
           select new Row(state, claim.Claimed, claim.Outcome, airs, margin);

    private Task LayAsync(int eventId, ReservationState state, DateTime starts, bool claimed, string? outcome, int margin)
        => ExecuteAsync(
            $"""
            INSERT INTO reservation (
                id, network_id, service_id, event_id, programme_start_at, rule_id, priority,
                start_at, end_at, end_at_confirmed, margin_before, margin_after,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres,
                snapshot_audio, snapshot_sounds, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                broadcast_group_key, broadcast_group_role, state, cancelled_because,
                started_at, recording_outcome, created_at)
            VALUES (
                '{Guid.NewGuid()}', 32736, 1024, {eventId}, '{Sql(starts)}', NULL, 10,
                '{Sql(starts)}', '{Sql(starts.AddMinutes(30))}', true, {margin}, {margin},
                'A programme', 'What it is about', '', '[]'::jsonb, 'Undetermined', 0, '{Sql(Made)}',
                false, '[]'::jsonb, false, NULL,
                NULL, 'Standalone', '{state}',
                {(state is ReservationState.Cancelled ? "'ByHand'" : "NULL")},
                {(claimed ? $"'{Sql(starts)}'" : "NULL")},
                {(outcome is null ? "NULL" : $"'{outcome}'")}, '{Sql(Made)}')
            """);

    private static string Sql(DateTime at) => at.ToString("yyyy-MM-dd HH:mm:sszzz", null);

    private async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = await OpenAsyncForTheMatrix();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<NpgsqlConnection> OpenAsyncForTheMatrix()
    {
        await using AsyncServiceScope scope = migrated.CreateAsyncScope();
        var connection = new NpgsqlConnection(Context(scope).Database.GetConnectionString());
        await connection.OpenAsync();

        return connection;
    }

    private readonly record struct Row(ReservationState State, bool Claimed, string? Outcome, TimeSpan Airs, int Margin);

    public sealed record StandingCase(
        ReservationStanding Standing,
        ReservationState State,
        bool Claimed,
        RecordingOutcome? Outcome);
}
