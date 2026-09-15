using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Reservations;

public sealed class ReservationRetryLineTests
{
    private static readonly DateTime Now = ReservationFactory.Now;

    [Fact]
    public void ARetryIsWrittenDownWithWhatCameOfIt()
    {
        Reservation reservation = ReservationFactory.Planned();

        ReservationOutcome line = ReservationOutcome.RecordRetry(
            ReservationOutcomeId.New(),
            reservation,
            RetryAttempt.Started,
            Now);

        Assert.Equal(ReservationOutcomeKind.Retried, line.Kind);
        Assert.Equal(RetryResult.Started, line.RetryResult);
        Assert.Null(line.GaveUpBecause);
        Assert.Null(line.TuneFailure);
        Assert.Empty(line.Faults);
        Assert.Equal(reservation.Id, line.ReservationId);
    }

    [Fact]
    public void ARetryRefusedAgainCarriesTheClassItWasRefusedIn()
    {
        ReservationOutcome line = Retry(
            RetryAttempt.RefusedAgain(RecordingStartFailure.TheTunerWouldNotTune(TuneFailureKind.NoLock)));

        Assert.Equal(RetryResult.RefusedAgain, line.RetryResult);
        Assert.Equal(TuneFailureKind.NoLock, line.TuneFailure);
        Assert.Equal([RecordingFault.TuneFailed], line.Faults);
    }

    [Fact]
    public void ARetryRefusedWithoutAClassCarriesNoneAndOneNobodyAnsweredSaysSo()
    {
        ReservationOutcome unnamed = Retry(RetryAttempt.RefusedAgain(null));
        ReservationOutcome unanswered = Retry(RetryAttempt.Unanswered);

        Assert.Equal(RetryResult.RefusedAgain, unnamed.RetryResult);
        Assert.Empty(unnamed.Faults);
        Assert.Null(unnamed.TuneFailure);
        Assert.Equal(RetryResult.NoAnswer, unanswered.RetryResult);
        Assert.Empty(unanswered.Faults);
    }

    [Fact]
    public void ARetryRefusedForWantOfATunerSaysSo()
        => Assert.Equal(
            [RecordingFault.TunerContended],
            Retry(RetryAttempt.RefusedAgain(RecordingStartFailure.NoTunerWasFree)).Faults);

    [Theory]
    [InlineData(RetryGiveUp.PrecheckFailed, true)]
    [InlineData(RetryGiveUp.CandidateNeedsAttention, false)]
    [InlineData(RetryGiveUp.BroadcastOver, false)]
    [InlineData(RetryGiveUp.AttemptsSpent, false)]
    public void GivingUpSaysWhyInTheClassesTheLedgerHolds(RetryGiveUp because, bool theDiskSaidNo)
    {
        IReadOnlyList<RecordingFault> faults = theDiskSaidNo ? [RecordingFault.RefusedByDiskPrecheck] : [];

        ReservationOutcome line = ReservationOutcome.RecordGivingUp(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            because,
            null,
            Now);

        Assert.Equal(ReservationOutcomeKind.GaveUpRetrying, line.Kind);
        Assert.Equal(because, line.GaveUpBecause);
        Assert.Null(line.RetryResult);
        Assert.Equal(faults, line.Faults);
        Assert.Null(line.TuneFailure);
    }

    [Fact]
    public void GivingUpOnAStructuralFailureNamesTheClassItWas()
    {
        ReservationOutcome line = ReservationOutcome.RecordGivingUp(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            RetryGiveUp.NotTransient,
            TuneFailureKind.StreamMismatch,
            Now);

        Assert.Equal(TuneFailureKind.StreamMismatch, line.TuneFailure);
        Assert.Equal([RecordingFault.TuneFailed], line.Faults);

        Assert.Throws<ArgumentException>(() => ReservationOutcome.RecordGivingUp(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            RetryGiveUp.NotTransient,
            null,
            Now));
    }

    [Fact]
    public void AClassIsNamedOnlyWhenTheReasonIsTheClass()
        => Assert.Throws<ArgumentException>(() => ReservationOutcome.RecordGivingUp(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            RetryGiveUp.BroadcastOver,
            TuneFailureKind.NoLock,
            Now));

    [Theory]
    [InlineData(ReservationOutcomeKind.Retried)]
    [InlineData(ReservationOutcomeKind.GaveUpRetrying)]
    public void TheGeneralRecordCannotWriteALineThatHasToSayWhatCameOfTheRetry(ReservationOutcomeKind kind)
        => Assert.Throws<ArgumentException>(() => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            kind,
            null,
            null,
            [],
            [],
            Now));

    [Fact]
    public void OnlyARetryCarriesAResultAndOnlyGivingUpCarriesAReason()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(ReservationOutcomeKind.Missed, RetryResult.Started, null));
        Assert.Throws<ArgumentException>(() => Rehydrated(ReservationOutcomeKind.Missed, null, RetryGiveUp.BroadcastOver));
        Assert.Throws<ArgumentException>(
            () => Rehydrated(ReservationOutcomeKind.Retried, RetryResult.Started, RetryGiveUp.BroadcastOver));
        Assert.Throws<ArgumentException>(() => Rehydrated(ReservationOutcomeKind.GaveUpRetrying, null, null));
    }

    [Fact]
    public void ARetryThatStartedASessionNamesNoFailure()
        => Assert.Throws<ArgumentException>(() => Rehydrated(
            ReservationOutcomeKind.Retried,
            RetryResult.Started,
            null,
            TuneFailureKind.NoLock,
            [RecordingFault.TuneFailed]));

    [Fact]
    public void ARetryThatNamesATuneFailureNamesTheClassTheRecorderGaveIt()
        => Assert.Throws<ArgumentException>(() => Rehydrated(
            ReservationOutcomeKind.Retried,
            RetryResult.RefusedAgain,
            null,
            TuneFailureKind.NoLock,
            [RecordingFault.TunerContended]));

    [Fact]
    public void AResultOrAReasonTheLedgerDoesNotHoldIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Rehydrated(ReservationOutcomeKind.Retried, (RetryResult)99, null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Rehydrated(ReservationOutcomeKind.GaveUpRetrying, null, (RetryGiveUp)99));
    }

    [Fact]
    public void RetryingAndGivingUpAreNotedOnTheWayAndSettleNothing()
    {
        Assert.Contains(ReservationOutcomeKind.Retried, ReservationOutcomeKinds.AlongTheWay);
        Assert.Contains(ReservationOutcomeKind.GaveUpRetrying, ReservationOutcomeKinds.AlongTheWay);
        Assert.DoesNotContain(ReservationOutcomeKind.Retried, ReservationOutcomeKinds.Settling);
        Assert.DoesNotContain(ReservationOutcomeKind.GaveUpRetrying, ReservationOutcomeKinds.Settling);
    }

    [Fact]
    public void AReservationWhoseStartNeverFailedInAClassHasNoHistoryToTryAgainFrom()
    {
        Reservation reservation = ReservationFactory.Planned();

        Assert.Null(StartRetry.HistoryOf([]));
        Assert.Null(StartRetry.HistoryOf(
        [
            ReservationOutcome.Record(ReservationOutcomeId.New(), reservation, ReservationOutcomeKind.Missed, null, null, [], [], Now),
            ReservationOutcome.Record(
                ReservationOutcomeId.New(),
                reservation,
                ReservationOutcomeKind.Competing,
                null,
                null,
                [RecordingFault.TunerContended],
                [],
                Now),
        ]));
    }

    [Fact]
    public void TheHistoryCountsTheAttemptsAfterTheFirstFailureAndKeepsTheLatest()
    {
        Reservation reservation = ReservationFactory.Planned();

        RetryHistory history = StartRetry.HistoryOf(
        [
            ReservationOutcome.RecordRetry(
                ReservationOutcomeId.New(),
                reservation,
                RetryAttempt.RefusedAgain(RecordingStartFailure.TheTunerWouldNotTune(TuneFailureKind.NoData)),
                Now.AddMinutes(2)),
            TuneFailure(reservation, TuneFailureKind.NoLock, Now),
            ReservationOutcome.RecordRetry(ReservationOutcomeId.New(), reservation, RetryAttempt.Unanswered, Now.AddMinutes(1)),
        ])!;

        Assert.Equal(2, history.AttemptsSoFar);
        Assert.Equal(Now.AddMinutes(2), history.LastAttemptAt);
        Assert.Equal([TuneFailureKind.NoLock, TuneFailureKind.NoData], history.FailureClasses);
        Assert.False(history.GivenUp);
    }

    [Fact]
    public void TheHistoryKnowsWhenTryingAgainWasGivenUp()
    {
        Reservation reservation = ReservationFactory.Planned();

        RetryHistory history = StartRetry.HistoryOf(
        [
            TuneFailure(reservation, TuneFailureKind.StreamMismatch, Now),
            ReservationOutcome.RecordGivingUp(
                ReservationOutcomeId.New(),
                reservation,
                RetryGiveUp.NotTransient,
                TuneFailureKind.StreamMismatch,
                Now.AddMinutes(1)),
        ])!;

        Assert.True(history.GivenUp);
        Assert.Equal(0, history.AttemptsSoFar);
        Assert.Equal(Now, history.LastAttemptAt);
    }

    private static ReservationOutcome Retry(RetryAttempt attempt)
        => ReservationOutcome.RecordRetry(ReservationOutcomeId.New(), ReservationFactory.Planned(), attempt, Now);

    private static ReservationOutcome TuneFailure(Reservation reservation, TuneFailureKind kind, DateTime at)
        => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            reservation,
            ReservationOutcomeKind.TuneFailure,
            kind,
            null,
            [RecordingFault.TuneFailed],
            [],
            at);

    private static ReservationOutcome Rehydrated(
        ReservationOutcomeKind kind,
        RetryResult? result,
        RetryGiveUp? because,
        TuneFailureKind? tuneFailure = null,
        IReadOnlyList<RecordingFault>? faults = null)
    {
        Reservation reservation = ReservationFactory.Planned();

        return ReservationOutcome.Rehydrate(
            ReservationOutcomeId.New(),
            reservation.Id,
            reservation.Programme,
            reservation.SnapshotName,
            reservation.EffectiveStartAt,
            reservation.EffectiveEndAt,
            reservation.Priority,
            reservation.RuleId,
            kind,
            tuneFailure,
            null,
            faults ?? [],
            [],
            Now,
            result,
            because);
    }
}
