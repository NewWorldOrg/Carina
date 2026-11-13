using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Unit.Reservations;

[Trait("Category", "Unit")]
public sealed class ReservationOutcomeTests
{
    private static readonly DateTime Now = ReservationFactory.Now;

    private static readonly DateTime Later = ReservationFactory.Now.AddDays(2);

    [Fact]
    public void AnOutcomeCarriesTheReservationItRecordsRatherThanPointingAtIt()
    {
        Reservation reservation = ReservationFactory.Planned(marginBefore: Margin.OfSeconds(10));

        ReservationOutcome outcome = ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            reservation,
            ReservationOutcomeKind.Missed,
            null,
            null,
            [],
            [],
            ReservationFactory.Now);

        Assert.Equal(reservation.Id, outcome.ReservationId);
        Assert.Equal(reservation.SnapshotName, outcome.SnapshotName);
        Assert.Equal(reservation.EffectiveStartAt, outcome.EffectiveStartAt);
        Assert.Equal(reservation.EffectiveEndAt, outcome.EffectiveEndAt);
        Assert.Equal(reservation.Priority, outcome.Priority);
    }

    [Fact]
    public void ATuneFailureIsRecordedWithWhichOfTheFourItWas()
    {
        Assert.Throws<ArgumentException>(
            () => Record(ReservationOutcomeKind.TuneFailure, null, null, [], [RecordingFault.TuneFailed]));

        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.TuneFailure,
            TuneFailureKind.IncompletePsi,
            null,
            [],
            [RecordingFault.TuneFailed]);

        Assert.Equal(TuneFailureKind.IncompletePsi, outcome.TuneFailure);
    }

    [Fact]
    public void OnlyAReservationThatLostAContestNamesWhatWasRecordedInstead()
    {
        Guid winner = Guid.NewGuid();

        Assert.Throws<ArgumentException>(() => Record(ReservationOutcomeKind.Missed, null, null, [winner]));

        ReservationOutcome outcome = Record(ReservationOutcomeKind.Competing, null, null, [winner]);

        Assert.Equal([winner], outcome.RecordedInstead);
    }

    [Fact]
    public void AFailureReportedByRecordingCarriesTheOutcomeRecordingWrote()
    {
        Assert.Throws<ArgumentException>(() => Record(ReservationOutcomeKind.RecordingFailure, null, null, []));

        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.RecordingFailure,
            null,
            RecordingOutcome.Truncated,
            []);

        Assert.Equal(RecordingOutcome.Truncated, outcome.RecordingOutcome);
    }

    [Fact]
    public void TheFourTuneFailuresAreKeptApart()
        => Assert.Equal(
            [
                TuneFailureKind.NoLock,
                TuneFailureKind.NoData,
                TuneFailureKind.IncompletePsi,
                TuneFailureKind.StreamMismatch,
            ],
            Enum.GetValues<TuneFailureKind>());

    [Fact]
    public void AFailureReportedByRecordingKeepsEveryClassItWasGiven()
    {
        ReservationOutcome outcome = Record(
            ReservationOutcomeKind.RecordingFailure,
            null,
            RecordingOutcome.Failed,
            [],
            [RecordingFault.NothingLanded, RecordingFault.ShortOfTheWindow]);

        Assert.Equal([RecordingFault.NothingLanded, RecordingFault.ShortOfTheWindow], outcome.Faults);
        Assert.Null(outcome.TuneFailure);
    }

    [Fact]
    public void ATuneFailureAndARecordingFailureAreToldApartByWhatEachOneCarries()
    {
        ReservationOutcome tuning = Record(
            ReservationOutcomeKind.TuneFailure,
            TuneFailureKind.NoLock,
            null,
            [],
            [RecordingFault.TuneFailed]);
        ReservationOutcome recording = Record(
            ReservationOutcomeKind.RecordingFailure,
            null,
            RecordingOutcome.Truncated,
            [],
            [RecordingFault.ShortOfTheWindow]);

        Assert.Null(tuning.RecordingOutcome);
        Assert.NotNull(tuning.TuneFailure);
        Assert.NotNull(recording.RecordingOutcome);
        Assert.Null(recording.TuneFailure);
    }

    [Fact]
    public void AClassificationTheLedgerDoesNotHoldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => Record(ReservationOutcomeKind.Missed, null, null, [], [(RecordingFault)99]));

    [Fact]
    public void ATuneFailureNamesTheClassTheRecorderGaveIt()
        => Assert.Throws<ArgumentException>(
            () => Record(
                ReservationOutcomeKind.TuneFailure,
                TuneFailureKind.NoLock,
                null,
                [],
                [RecordingFault.TunerContended]));

    [Fact]
    public void EveryClassificationTheLedgerHoldsEitherSettlesTheReservationOrIsNotedOnTheWayToIt()
    {
        ReservationOutcomeKind[] held = [.. Enum.GetValues<ReservationOutcomeKind>().Order()];
        ReservationOutcomeKind[] named =
            [.. ReservationOutcomeKinds.Settling.Concat(ReservationOutcomeKinds.AlongTheWay).Order()];

        Assert.Equal(held, named);
    }

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

    [Fact]
    public void ARecordingThatCameOutWholeButScrambledIsAFailureOfTheRecording()
    {
        ReservationOutcome line = Scrambled(RecordingOutcome.Complete);

        Assert.Equal(ReservationOutcomeKind.RecordingFailure, line.Kind);
        Assert.Equal(RecordingOutcome.Complete, line.RecordingOutcome);
        Assert.Equal([RecordingFault.ScramblingUnresolved], line.Faults);
        Assert.True(line.LeftScrambled);
        Assert.Null(line.DescrambledAt);
    }

    [Fact]
    public void ARecordingThatCameOutWholeIsAFailureOnlyWhenItWasLeftScrambled()
    {
        Assert.Throws<ArgumentException>(
            () => RecordWithFaults(ReservationOutcomeKind.RecordingFailure, RecordingOutcome.Complete, []));
        Assert.Throws<ArgumentException>(
            () => RecordWithFaults(
                ReservationOutcomeKind.RecordingFailure,
                RecordingOutcome.Complete,
                [RecordingFault.HeavierThanTheStream]));

        ReservationOutcome line = RecordWithFaults(
            ReservationOutcomeKind.RecordingFailure,
            RecordingOutcome.Complete,
            [RecordingFault.HeavierThanTheStream, RecordingFault.ScramblingUnresolved]);

        Assert.True(line.LeftScrambled);
    }

    [Fact]
    public void ALineThatNamesNoScramblingIsNotLeftScrambled()
    {
        ReservationOutcome line = RecordWithFaults(
            ReservationOutcomeKind.RecordingFailure,
            RecordingOutcome.Truncated,
            [RecordingFault.ShortOfTheWindow]);

        Assert.False(line.LeftScrambled);
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void DescramblingLiftsTheLineAndKeepsWhatItSaid(RecordingOutcome outcome)
    {
        ReservationOutcome line = Scrambled(outcome);

        line.Descrambled(Later);

        Assert.False(line.LeftScrambled);
        Assert.Equal(Later, line.DescrambledAt);
        Assert.Equal(ReservationOutcomeKind.RecordingFailure, line.Kind);
        Assert.Equal(outcome, line.RecordingOutcome);
        Assert.Equal([RecordingFault.ScramblingUnresolved], line.Faults);
    }

    [Fact]
    public void ALineThatWasNeverLeftScrambledIsNotDescrambled()
    {
        ReservationOutcome line = RecordWithFaults(ReservationOutcomeKind.Missed, null, []);

        Assert.Throws<InvalidOperationException>(() => line.Descrambled(Later));
        Assert.Null(line.DescrambledAt);
    }

    [Fact]
    public void ALineIsDescrambledOnce()
    {
        ReservationOutcome line = Scrambled(RecordingOutcome.Complete);
        line.Descrambled(Later);

        Assert.Throws<InvalidOperationException>(() => line.Descrambled(Later.AddDays(1)));
        Assert.Equal(Later, line.DescrambledAt);
    }

    [Fact]
    public void ALineIsNotDescrambledBeforeItWasWritten()
    {
        ReservationOutcome line = Scrambled(RecordingOutcome.Complete);

        Assert.Throws<ArgumentException>(() => line.Descrambled(ReservationFactory.Now.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(
            () => line.Descrambled(DateTime.SpecifyKind(Later, DateTimeKind.Local)));
        Assert.True(line.LeftScrambled);
    }

    [Fact]
    public void ARowReadBackKeepsWhenItWasDescrambled()
    {
        ReservationOutcome again = Again(Scrambled(RecordingOutcome.Complete), Later);

        Assert.Equal(Later, again.DescrambledAt);
        Assert.False(again.LeftScrambled);
    }

    [Fact]
    public void ARowCannotSayItWasDescrambledUnlessItNamedTheScramblingBeforeThat()
    {
        ReservationOutcome truncated = RecordWithFaults(
            ReservationOutcomeKind.RecordingFailure,
            RecordingOutcome.Truncated,
            [RecordingFault.ShortOfTheWindow]);

        Assert.Throws<ArgumentException>(() => Again(truncated, Later));
        Assert.Throws<ArgumentException>(
            () => Again(Scrambled(RecordingOutcome.Complete), ReservationFactory.Now.AddSeconds(-1)));
    }

    private static ReservationOutcome Record(
        ReservationOutcomeKind kind,
        TuneFailureKind? tuneFailure,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<Guid> recordedInstead,
        IReadOnlyList<RecordingFault>? faults = null)
        => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            kind,
            tuneFailure,
            recordingOutcome,
            faults ?? [],
            recordedInstead,
            ReservationFactory.Now);

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

    private static ReservationOutcome Scrambled(RecordingOutcome outcome)
        => RecordWithFaults(ReservationOutcomeKind.RecordingFailure, outcome, [RecordingFault.ScramblingUnresolved]);

    private static ReservationOutcome RecordWithFaults(
        ReservationOutcomeKind kind,
        RecordingOutcome? recordingOutcome,
        IReadOnlyList<RecordingFault> faults)
        => ReservationOutcome.Record(
            ReservationOutcomeId.New(),
            ReservationFactory.Planned(),
            kind,
            null,
            recordingOutcome,
            faults,
            [],
            ReservationFactory.Now);

    private static ReservationOutcome Again(ReservationOutcome held, DateTime? descrambledAt)
        => ReservationOutcome.Rehydrate(
            held.Id,
            held.ReservationId,
            new(held.NetworkId, held.ServiceId, held.EventId, held.ProgrammeStartsAt),
            held.SnapshotName,
            held.EffectiveStartAt,
            held.EffectiveEndAt,
            held.Priority,
            held.RuleId,
            held.Kind,
            held.TuneFailure,
            held.RecordingOutcome,
            held.Faults,
            held.RecordedInstead,
            held.OccurredAt,
            held.RetryResult,
            held.GaveUpBecause,
            descrambledAt);
}
