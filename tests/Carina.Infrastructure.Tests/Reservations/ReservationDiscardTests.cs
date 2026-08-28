using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Tests.Reservations;

[Collection(RepositoryDatabaseCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationDiscardTests(RepositoryDatabase database)
{
    private static readonly DateTime Now = ReservationFixtures.Now;

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task AReservationThatWasCancelledLeavesTheTableAltogether()
    {
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation beside = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        await AddAsync(cancelled, beside);

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
        await AddAsync(standing);

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
        await AddAsync(standing);

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
        await AddAsync(over);

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
        await AddAsync(standing);

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
        await AddAsync(standing);

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
        await AddAsync(standing);

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
        await AddAsync(without, waiting);

        DateTime between = ends.AddSeconds(300);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(without.Id, between));
        Assert.Equal(ReservationDiscard.StillToBeRecorded, await DiscardAsync(waiting.Id, between));
    }

    [Fact]
    public async Task AReservationStoppedEarlyGoesOnceItsRecordingIsGoneThoughItsWindowIsOpen()
    {
        Reservation recorded = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsync(recorded);
        await ClaimAsync(recorded.Id);
        Recording written = await RecordedAsync(recorded, settled: true);

        await using (CarinaDbContext throwing = database.Open())
        {
            Assert.Equal(
                RecordingDiscard.Discarded,
                await new RecordingDirectory(throwing).DiscardAsync(written.Id, Cancel));
        }

        Assert.True(recorded.EffectiveEndAt > Now);
        Assert.Equal(RecordingOutcome.Complete, await OutcomeAsync(recorded.Id));
        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(recorded.Id));
    }

    [Fact]
    public async Task AMomentThatIsNotInUtcIsRefusedRatherThanComparedAgainstTheWindow()
    {
        Reservation standing = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        await AddAsync(standing);

        await using CarinaDbContext discarding = database.Open();
        ArgumentException refused = await Assert.ThrowsAsync<ArgumentException>(
            () => new ReservationRepository(discarding).DiscardAsync(
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
        await AddAsync(recorded);
        await ClaimAsync(recorded.Id);
        Recording written = await RecordedAsync(recorded, settled: true);

        Assert.Equal(ReservationDiscard.RecordingCameOfIt, await DiscardAsync(recorded.Id));
        Assert.Equal(1, await ReservationRows(recorded.Id));
        Assert.Equal(RecordingOutcome.Complete, await OutcomeAsync(recorded.Id));

        await using (CarinaDbContext throwing = database.Open())
        {
            Assert.Equal(
                RecordingDiscard.Discarded,
                await new RecordingDirectory(throwing).DiscardAsync(written.Id, Cancel));
        }

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(recorded.Id));
        Assert.Equal(0, await ReservationRows(recorded.Id));
    }

    [Fact]
    public async Task AReservationBeingWrittenRightNowIsRefusedBecauseItsRecordingIsThere()
    {
        Reservation writing = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsync(writing);
        await ClaimAsync(writing.Id);
        Recording begun = await RecordedAsync(writing, settled: false);

        Assert.Equal(ReservationDiscard.RecordingCameOfIt, await DiscardAsync(writing.Id));
        Assert.Equal(1, await ReservationRows(writing.Id));
        Assert.Equal(1, await RecordingRows(begun.Id));
    }

    [Fact]
    public async Task AReservationTakenUpBeforeItsRecordingIsWrittenDownIsRefused()
    {
        Reservation claimed = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsync(claimed);

        await ClaimAsync(claimed.Id);

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
        await AddAsync(cancelled);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(cancelled.Id));
        Assert.Equal(ReservationDiscard.NoSuchReservation, await DiscardAsync(cancelled.Id));
    }

    [Fact]
    public async Task ThrowingAReservationAwayLeavesTheRecordingsOfEveryOtherReservationWhereTheyAre()
    {
        Reservation cancelled = ReservationFixtures.Rehydrated(ReservationState.Cancelled);
        Reservation recorded = ReservationFixtures.Rehydrated(ReservationState.Scheduled);
        await AddAsync(cancelled, recorded);
        await ClaimAsync(recorded.Id);
        Recording beside = await RecordedAsync(recorded, settled: true);

        Assert.Equal(ReservationDiscard.Discarded, await DiscardAsync(cancelled.Id));

        Assert.Equal(1, await RecordingRows(beside.Id));
        Assert.Equal(1, await ReservationRows(recorded.Id));
    }

    private static Reservation Closed(ReservationState state)
        => ReservationFixtures.Rehydrated(state, startAt: Now.AddHours(-3), endAt: Now.AddHours(-2));

    private async Task ClaimAsync(ReservationId id)
    {
        await using CarinaDbContext claiming = database.Open();

        Assert.True(await new ReservationRecordingContract(claiming).ClaimAsync(id, Now, Cancel));
    }

    private async Task<ReservationDiscard> DiscardAsync(ReservationId id, DateTime? at = null)
    {
        await using CarinaDbContext discarding = database.Open();

        return await new ReservationRepository(discarding).DiscardAsync(id, at ?? Now, Cancel);
    }

    private async Task<int> ReservationRows(ReservationId id)
    {
        await using CarinaDbContext counting = database.Open();

        return await counting.Set<Reservation>().CountAsync(reservation => reservation.Id == id, Cancel);
    }

    private async Task<int> RecordingRows(RecordingId id)
    {
        await using CarinaDbContext counting = database.Open();

        return await counting.Set<Recording>().CountAsync(recording => recording.Id == id, Cancel);
    }

    private async Task<RecordingOutcome?> OutcomeAsync(ReservationId id)
    {
        await using CarinaDbContext reading = database.Open();
        Reservation? found = await new ReservationRepository(reading).FindAsync(id, Cancel);

        return found?.RecordingOutcome;
    }

    private async Task AddAsync(params Reservation[] reservations)
    {
        await using CarinaDbContext writing = database.Open();
        var repository = new ReservationRepository(writing);

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
            new ProgrammeSnapshot("A programme", string.Empty, string.Empty, [], Now),
            null,
            BroadcastGroupRole.Standalone,
            Now,
            new TunerDeviceId("adapter0"));

        await using CarinaDbContext writing = database.Open();
        var repository = new RecordingRepository(writing);
        await repository.AddAsync(begun, Cancel);

        if (settled)
        {
            begun.Abort(reservation.EffectiveEndAt);
            begun.Settle(RecordingOutcome.Complete, 1_000_000, reservation.EffectiveEndAt);
            await repository.SaveAsync(begun, Cancel);
        }

        return begun;
    }
}
