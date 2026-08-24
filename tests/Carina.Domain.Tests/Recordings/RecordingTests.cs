using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact]
    public void ARecordingBeginsWithNoOutcomeAtAll()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Null(recording.Outcome);
        Assert.True(recording.IsInFlight);
        Assert.Equal(DropCounters.Unmeasured, recording.Counters);
        Assert.Null(recording.ScrambledPackets);
        Assert.Null(recording.FileSizeObserved);
        Assert.Empty(recording.Interruptions);
        Assert.Empty(recording.OutcomeDetail);
    }

    [Fact]
    public void ARecordingKeepsNoPublicConstructorBesideRehydrate()
        => Assert.Empty(typeof(Recording).GetConstructors());

    [Fact]
    public void ARecordingWithoutAReservationIsStillARecording()
    {
        Recording recording = RecordingFactory.Started(reservationId: null);

        Assert.Null(recording.ReservationId);
    }

    [Fact]
    public void ARecordingCarriesTheBroadcastGroupItStartedIn()
    {
        var key = new BroadcastGroupKey("32736-1024-4001");

        Recording recording = RecordingFactory.Started(
            groupKey: key,
            groupRole: BroadcastGroupRole.MovementPrimary);

        Assert.Equal(key, recording.BroadcastGroupKey);
        Assert.Equal(BroadcastGroupRole.MovementPrimary, recording.BroadcastGroupRole);
    }

    [Fact]
    public void ARecordingInAGroupNamesTheBroadcastItBelongsTo()
        => Assert.Throws<ArgumentException>(
            () => RecordingFactory.Started(groupRole: BroadcastGroupRole.RelaySegment));

    [Fact]
    public void ARecordingFileNameThatNamesAnotherRecordingIsRefused()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Begin(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(RecordingId.New(), ".m2ts"),
            Now,
            Now.AddHours(1),
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now));

        Assert.Equal("fileName", refusal.ParamName);
    }

    [Fact]
    public void ARecordingWindowEndsAfterItStarts()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Begin(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Now));

        Assert.Equal("expectedWindowEnd", refusal.ParamName);
    }

    [Fact]
    public void TimesAreKeptInUtc()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Begin(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Now,
            Now.AddHours(1),
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            DateTime.SpecifyKind(Now, DateTimeKind.Local)));

        Assert.Equal("startedAtActual", refusal.ParamName);
    }

    [Fact]
    public void WhatWasWrittenAddsUpAcrossResumes()
    {
        Recording recording = RecordingFactory.Started();

        recording.Wrote(TimeSpan.FromMinutes(10));
        recording.Wrote(TimeSpan.FromMinutes(12));

        Assert.Equal(TimeSpan.FromMinutes(22), recording.Written);
        Assert.Equal(1_320_000, recording.WrittenDurationMs);
    }

    [Fact]
    public void ARecordingCannotUnwriteWhatItWrote()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingFactory.Started().Wrote(TimeSpan.FromSeconds(-1)));

    [Fact]
    public void AnInterruptionIsClosedByTheResumeThatFollowsIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Interrupt(RecordingFault.DriverLost, Now);
        Assert.Null(Assert.Single(recording.Interruptions).ResumedAt);
        Assert.Equal(0, recording.ResumeCount);

        recording.Resume(Now.AddSeconds(12));

        Assert.Equal(Now.AddSeconds(12), Assert.Single(recording.Interruptions).ResumedAt);
        Assert.Equal(1, recording.ResumeCount);
    }

    [Fact]
    public void AnUnresumedRecordingIsNotInterruptedTwice()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now);

        Assert.Throws<InvalidOperationException>(() => recording.Interrupt(RecordingFault.DriverLost, Now));
    }

    [Fact]
    public void ARecordingThatWasNeverInterruptedIsNotResumed()
        => Assert.Throws<InvalidOperationException>(() => RecordingFactory.Started().Resume(Now));

    [Fact]
    public void MeasuringWritesBothTheCountAndWhenItWasTaken()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 12, 3, Now.AddMinutes(1));

        Assert.Equal(DropCounters.Counted(4, 900), recording.Counters);
        Assert.Equal(12, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
        Assert.Equal(Now.AddMinutes(1), recording.MeasuredUpdatedAt);
    }

    [Fact]
    public void MeasurementThatBreaksMidWayGoesBackToUnmeasuredRatherThanToZero()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 12, 3, Now.AddMinutes(1));

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 3, Now.AddMinutes(2));

        Assert.False(recording.Counters.Measured);
        Assert.Null(recording.Counters.Dropped);
        Assert.Null(recording.Counters.Total);
        Assert.Null(recording.ScrambledPackets);
    }

    [Fact]
    public void CountersThatWereTakenSayWhenTheyWereTaken()
        => Assert.Throws<ArgumentException>(() => Rehydrated(
            counters: DropCounters.Counted(1, 10),
            measuredUpdatedAt: null));

    [Fact]
    public void ARecordingFollowsAProgrammeLaterButNeverEarlier()
    {
        Recording recording = RecordingFactory.Started();
        DateTime was = recording.ExpectedWindowEnd;

        recording.Extend(was.AddMinutes(15));

        Assert.Equal(was.AddMinutes(15), recording.ExpectedWindowEnd);
        Assert.Throws<ArgumentException>(() => recording.Extend(was));
        Assert.Throws<ArgumentException>(() => recording.Extend(recording.ExpectedWindowEnd));
    }

    [Fact]
    public void AnEndNobodyAskedForIsNotAComplete()
    {
        Recording recording = RecordingFactory.Started();
        recording.Wrote(TimeSpan.FromMinutes(60));

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(1)));

        Assert.Equal("outcome", refusal.ParamName);
        Assert.Null(recording.Outcome);
    }

    [Fact]
    public void OnlyTheStopThisSideAskedForReachesComplete()
    {
        Recording recording = RecordingFactory.Started();
        recording.Wrote(TimeSpan.FromMinutes(60));
        recording.Abort(Now.AddHours(1));

        recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(1));

        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.False(recording.IsInFlight);
        Assert.Equal(3_400_000_000, recording.FileSizeObserved);
        Assert.Equal(Now.AddHours(1), recording.ObservedAt);
        Assert.Equal(Now.AddHours(1), recording.StoppedAtActual);
    }

    [Fact]
    public void AnEmptyFileIsAFailureEvenWhenThisSideAskedItToStop()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now.AddHours(1));
        recording.Note(RecordingFactory.Fault());

        Assert.Throws<ArgumentException>(() => recording.Settle(RecordingOutcome.Complete, 0, Now.AddHours(1)));
        Assert.Throws<ArgumentException>(() => recording.Settle(RecordingOutcome.Truncated, 0, Now.AddHours(1)));

        recording.Settle(RecordingOutcome.Failed, 0, Now.AddHours(1));

        Assert.Equal(RecordingOutcome.Failed, recording.Outcome);
    }

    [Fact]
    public void AnEndingThatIsNotACompleteSaysWhyInClasses()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<ArgumentException>(
            () => recording.Settle(RecordingOutcome.Truncated, 1_200_000, Now.AddHours(1)));

        recording.Note(new OutcomeDetail(
            RecordingFault.TuneFailed,
            TuneFailureKind.IncompletePsi,
            "the service never appeared in the PMT",
            Now));

        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Now.AddHours(1));

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        OutcomeDetail detail = Assert.Single(recording.OutcomeDetail);
        Assert.Equal(RecordingFault.TuneFailed, detail.Fault);
        Assert.Equal(TuneFailureKind.IncompletePsi, detail.TuneFailure);
    }

    [Fact]
    public void TheDetailIsAListOfClassesRatherThanOneSentence()
    {
        Recording recording = RecordingFactory.Started();

        recording.Note(new OutcomeDetail(RecordingFault.TunerContended, null, string.Empty, Now));
        recording.Note(new OutcomeDetail(RecordingFault.DiskExhausted, null, "no space left", Now.AddMinutes(1)));

        recording.Settle(RecordingOutcome.Failed, 12, Now.AddHours(1));

        Assert.Equal(
            [RecordingFault.TunerContended, RecordingFault.DiskExhausted],
            recording.OutcomeDetail.Select(detail => detail.Fault));
    }

    [Fact]
    public void AFaultTheLedgerDoesNotHoldIsRefused()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<ArgumentOutOfRangeException>(() => recording.Interrupt((RecordingFault)99, Now));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => recording.Note(new OutcomeDetail((RecordingFault)99, null, string.Empty, Now)));
    }

    [Fact]
    public void AnOutcomeIsWrittenOnce()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Now.AddHours(1));

        Assert.Throws<InvalidOperationException>(() => recording.Abort(Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(
            () => recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(2)));
        Assert.Throws<InvalidOperationException>(() => recording.Wrote(TimeSpan.FromMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => recording.Extend(Now.AddHours(3)));
        Assert.Throws<InvalidOperationException>(() => recording.Interrupt(RecordingFault.DriverLost, Now));
    }

    [Fact]
    public void AnOutcomeTheLedgerDoesNotHoldIsRefused()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now.AddHours(1));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => recording.Settle((RecordingOutcome)0, 12, Now.AddHours(1)));
    }

    [Fact]
    public void ARehydratedCompleteWithoutAnAbortIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Rehydrated(
            outcome: RecordingOutcome.Complete,
            abortedAt: null,
            stoppedAtActual: Now.AddHours(1),
            fileSizeObserved: 3_400_000_000,
            observedAt: Now.AddHours(1)));

        Assert.Equal("outcome", refusal.ParamName);
    }

    [Fact]
    public void ARehydratedOutcomeWithoutASizeIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Rehydrated(
            outcome: RecordingOutcome.Failed,
            stoppedAtActual: Now.AddHours(1),
            outcomeDetail: [RecordingFactory.Fault()]));

        Assert.Equal("fileSizeObserved", refusal.ParamName);
    }

    [Fact]
    public void ASizeReadOffTheDiskSaysWhenItWasRead()
    {
        Assert.Equal(
            "observedAt",
            Assert.Throws<ArgumentException>(() => Rehydrated(fileSizeObserved: 12)).ParamName);
        Assert.Equal(
            "observedAt",
            Assert.Throws<ArgumentException>(() => Rehydrated(observedAt: Now)).ParamName);
    }

    [Fact]
    public void ARehydratedOutcomeWithoutAStopTimeIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Rehydrated(
            outcome: RecordingOutcome.Failed,
            stoppedAtActual: null,
            fileSizeObserved: 0,
            observedAt: Now.AddHours(1),
            outcomeDetail: [RecordingFactory.Fault()]));

        Assert.Equal("stoppedAtActual", refusal.ParamName);
    }

    [Fact]
    public void ARehydratedRecordingCarriesTheHistoryItWasStoredWith()
    {
        RecordingId id = RecordingId.New();

        Recording recording = Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            3_400_000_000,
            Now.AddHours(1),
            Now.AddMinutes(-5),
            Now.AddHours(1),
            Now.AddHours(1),
            3_600_000,
            2,
            [
                new Interruption(RecordingFault.DriverLost, Now, Now.AddSeconds(9)),
                new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), Now.AddMinutes(4).AddSeconds(3)),
            ],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            RecordingOutcome.Complete,
            [],
            DropCounters.Counted(4, 900),
            DropTimeline.Unlocated,
            12,
            3,
            Now.AddHours(1),
            RecordingFactory.Tuner,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);

        Assert.Equal(2, recording.ResumeCount);
        Assert.Equal(2, recording.Interruptions.Count);
        Assert.Equal(TimeSpan.FromHours(1), recording.Written);
        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.Equal("A programme", recording.SnapshotName);
        Assert.Equal(RecordingFactory.Programme(), recording.Programme);
    }

    private static Recording Rehydrated(
        RecordingOutcome? outcome = null,
        DateTime? abortedAt = null,
        DateTime? stoppedAtActual = null,
        long? fileSizeObserved = null,
        DateTime? observedAt = null,
        IReadOnlyList<OutcomeDetail>? outcomeDetail = null,
        DropCounters? counters = null,
        DropTimeline? positions = null,
        long? scrambledPackets = null,
        DateTime? measuredUpdatedAt = null)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            fileSizeObserved,
            observedAt,
            Now.AddMinutes(-5),
            stoppedAtActual,
            abortedAt,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            outcome,
            outcomeDetail ?? [],
            counters ?? DropCounters.Unmeasured,
            positions ?? DropTimeline.Unlocated,
            scrambledPackets,
            0,
            measuredUpdatedAt,
            RecordingFactory.Tuner,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);
    }
}
