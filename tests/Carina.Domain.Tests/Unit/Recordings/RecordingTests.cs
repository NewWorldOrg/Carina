using System.Collections;
using System.Globalization;
using System.Reflection;

using Carina.Domain.Base;
using Carina.Domain.Captions;
using Carina.Domain.Channels;
using Carina.Domain.DataBroadcast;
using Carina.Domain.Programmes;
using Carina.Domain.Quality;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Puppis.Contracts;

using static Carina.Domain.Tests.Unit.Recordings.RecordingFactory;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class RecordingTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    public static TheoryData<long?, int, bool> Endings => new()
    {
        { CompletionFactory.TypicalBytes, 1000, true },
        { 0, 1000, true },
        { null, 1000, true },
        { CompletionFactory.TypicalBytes, 1000, false },
        { 2_400_000_000, 960, true },
        { 1_700_000_000, 1000, true },
        { 3_300_000_001, 1000, true },
        { 2_250_000_000, 900, true },
    };

    private static readonly DateTime Later = RecordingFactory.Now.AddHours(2);

    private static readonly DateTime Began = RecordingFactory.Now;

    private static readonly DateTime Reopened = Began.AddMinutes(10);

    private static readonly DateTime BeganAtTheSource = new(2026, 9, 8, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime EndedAtTheSource = new(2026, 9, 8, 4, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime CarriedAt = new(2026, 9, 8, 5, 0, 0, DateTimeKind.Utc);

    private static readonly ProgrammeRef Programme =
        new(new NetworkId(32736), new ServiceId(1024), new EventId(4321), BeganAtTheSource);

    private static readonly DateTime EndsInAnHour = RecordingFactory.Now.AddHours(1);

    private static readonly DateTime EndedAfterAnHour = RecordingFactory.Now.AddHours(1);

    private static readonly DateTime TwoDaysAfterItEnded = EndedAfterAnHour.AddDays(2);

    private static readonly DriverHello Driver =
        new(
            DriverProtocol.Version,
            "measuring",
            [
                DriverCapabilities.Recording,
                DriverCapabilities.CcMeasurement,
                DriverCapabilities.ScrambleMeasurement,
                DriverCapabilities.DropPositions,
            ]);

    private static readonly DateTime AnHourLater = RecordingFactory.Now.AddHours(1);

    private static readonly QualityBands Bands =
        QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], RecordingFactory.Now));

    private static readonly HashSet<string> Derived = new(StringComparer.Ordinal)
    {
        nameof(Recording.ThumbnailShowsAnUnfinishedRecording),
        nameof(Recording.LeftScrambled),
        nameof(Recording.Encodability),
        nameof(Recording.CanBeWatched),
        nameof(Recording.FollowsItsProgramme),
        nameof(Recording.CountedUntil),
        nameof(Recording.IsInterrupted),
        nameof(Recording.AwaitsAThumbnail),
        nameof(Recording.AwaitsCaptions),
        nameof(Recording.AwaitsItsDataBroadcast),
        nameof(Recording.DataBroadcastNeverCameDue),
        nameof(Recording.DataBroadcastIsDueAgain),
    };

    private static readonly DateTime Starts = Now.AddMinutes(-5);

    private static readonly DateTime Ends = Now.AddMinutes(55);

    private static readonly DateTime HalfAnHourLater = Now.AddMinutes(30);

    private static readonly QualityBands AsShipped = QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], Now));

    public static TheoryData<long?> EverySize => new(null, 0L, 1L, 4_096_000L);

    private static readonly DateTime Local = DateTime.SpecifyKind(Now.AddMinutes(1), DateTimeKind.Local);

    private static readonly DateTime Unspecified =
        DateTime.SpecifyKind(Now.AddMinutes(1), DateTimeKind.Unspecified);

    private static readonly DateTime Noon = new(2026, 9, 17, 3, 0, 0, DateTimeKind.Utc);

    public static TheoryData<RecordingFault> Breaking => Set(RecordingFaults.ThatCanInterrupt);

    public static TheoryData<RecordingFault> NotBreaking
        => Set(Enum.GetValues<RecordingFault>().Except(RecordingFaults.ThatCanInterrupt));

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
    public void ARecordingKeepsTheSoundTheBroadcastAnnouncedWhenItBegan()
    {
        Recording recording = RecordingFactory.Started(
            snapshot: RecordingFactory.Snapshot(AudioMode.DualMono, 2));

        Assert.Equal(AudioMode.DualMono, recording.SnapshotAudio);
        Assert.Equal(2, recording.SnapshotSounds);
    }

    [Fact]
    public void ARecordingOfABroadcastThatAnnouncedNoSoundSaysSoRatherThanGuessing()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(AudioMode.Undetermined, recording.SnapshotAudio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, recording.SnapshotSounds);
    }

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
            Now.AddMinutes(55),
            RecordingOutcome.Complete,
            [],
            DropCounters.Counted(4, 900),
            DropTimeline.Unlocated,
            12,
            3,
            Now.AddHours(1),
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
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
            Now.AddMinutes(55),
            outcome,
            outcomeDetail ?? [],
            counters ?? DropCounters.Unmeasured,
            positions ?? DropTimeline.Unlocated,
            scrambledPackets,
            0,
            measuredUpdatedAt,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);
    }

    [Theory]
    [MemberData(nameof(Endings))]
    public void AVerdictIsWrittenToTheLedgerWithoutTranslation(long? bytes, int writtenSeconds, bool asked)
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(
            bytes,
            TimeSpan.FromSeconds(writtenSeconds),
            asked);
        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Recording recording = Settle(evidence, verdict);

        Assert.Equal(verdict.Outcome, recording.Outcome);
        Assert.Equal(verdict.Faults, recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void ARecordingThisSideAskedToStopIsWrittenDownAsComplete()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence();
        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Recording recording = Settle(evidence, verdict);

        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.Empty(recording.OutcomeDetail);
    }

    [Fact]
    public void ARecordingNobodyAskedToStopIsWrittenDownWithTheReasonItIsNotComplete()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(asked: false);
        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Recording recording = Settle(evidence, verdict);

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        Assert.Equal([RecordingFault.StoppedUnasked], recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void AnEmptyFileAndAnUnweighedOneAreToldApartInTheLedger()
    {
        RecordingEvidence empty = CompletionFactory.Evidence(bytes: 0);
        RecordingEvidence unweighed = CompletionFactory.Evidence(bytes: null);

        Recording emptied = Settle(empty, CompletionFactory.Judge(empty));
        Recording unknown = Settle(unweighed, CompletionFactory.Judge(unweighed));

        Assert.Equal(RecordingOutcome.Failed, emptied.Outcome);
        Assert.Equal(RecordingOutcome.Failed, unknown.Outcome);
        Assert.Equal([RecordingFault.NothingLanded], emptied.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        Assert.Equal([RecordingFault.SizeUnobserved], unknown.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    [Fact]
    public void EveryFaultTheVerdictCanNameIsOneTheLedgerAccepts()
    {
        foreach (RecordingFault fault in CompletionFactory.FaultsTheCrossCheckNames)
        {
            Recording recording = RecordingFactory.Started();
            recording.Note(new OutcomeDetail(fault, null, string.Empty, Now));

            Assert.Equal([fault], recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
        }
    }

    private static Recording Settle(RecordingEvidence evidence, RecordingVerdict verdict)
    {
        Recording recording = RecordingFactory.Started();

        if (evidence.AbortedAt is not null)
        {
            recording.Abort(Now);
        }

        foreach (OutcomeDetail detail in verdict.Detail(Now))
        {
            recording.Note(detail);
        }

        recording.Settle(verdict.Outcome, evidence.FileSizeBytes ?? 0, Now);

        return recording;
    }

    [Fact(DisplayName = "BR-KD-012: no picture is drawn of a recording still being written")]
    public void NoPictureIsDrawnOfARecordingStillBeingWritten()
        => Assert.False(RecordingFactory.Started().AwaitsAThumbnail);

    [Theory(DisplayName = "BR-KD-012: a picture is drawn of a recording that has ended, once, until it is asked for again")]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void APictureIsDrawnOfARecordingThatHasEnded(RecordingOutcome outcome)
    {
        Recording recording = Ended(outcome);

        Assert.True(recording.AwaitsAThumbnail);

        recording.Illustrate(ThumbnailState.Skipped);

        Assert.False(recording.AwaitsAThumbnail);

        recording.Illustrate(ThumbnailState.Pending);

        Assert.True(recording.AwaitsAThumbnail);
    }

    [Theory(DisplayName = "BR-KD-012: a picture drawn, skipped or that could not be drawn is not drawn again unasked")]
    [InlineData(ThumbnailState.Ready, null)]
    [InlineData(ThumbnailState.Skipped, null)]
    [InlineData(ThumbnailState.Failed, ThumbnailFault.TimedOut)]
    public void APictureSettledIsNotDrawnAgain(ThumbnailState state, ThumbnailFault? fault)
    {
        Recording recording = Ended(RecordingOutcome.Truncated);

        recording.Illustrate(state, fault);

        Assert.False(recording.AwaitsAThumbnail);
    }

    [Fact(DisplayName = "BR-PD-016: captions are not taken from a recording still being written")]
    public void CaptionsAreNotTakenWhileARecordingIsWritten()
        => Assert.False(RecordingFactory.Started().AwaitsCaptions);

    [Fact(DisplayName = "BR-PD-016: captions are taken from a recording once it has ended, and not again once they are ready or absent")]
    public void CaptionsAreTakenOnceARecordingHasEnded()
    {
        Recording ready = Ended(RecordingOutcome.Truncated);
        Recording absent = Ended(RecordingOutcome.Truncated);

        Assert.True(ready.AwaitsCaptions);

        ready.Caption(CaptionState.Ready, 3, Later);
        absent.Caption(CaptionState.Absent, null, Later);

        Assert.False(ready.AwaitsCaptions);
        Assert.False(absent.AwaitsCaptions);
    }

    [Fact(DisplayName = "BR-PD-016: captions that failed are taken again until they have failed as many times in a row as are tried")]
    public void CaptionsThatFailedAreTakenAgainUntilTheTriesRunOut()
    {
        Recording recording = Ended(RecordingOutcome.Truncated);

        for (int failed = 1; failed < CaptionSettings.TriesAtMost; failed++)
        {
            recording.Caption(CaptionState.Failed, null, Later.AddMinutes(failed));

            Assert.True(recording.AwaitsCaptions);
        }

        recording.Caption(CaptionState.Failed, null, Later.AddHours(1));

        Assert.False(recording.AwaitsCaptions);
    }

    [Fact(DisplayName = "BR-PD-016: captions taken before the file was descrambled are taken again, and not when both happened at the same instant")]
    public void CaptionsTakenBeforeTheDescramblingAreTakenAgain()
    {
        Recording before = Ended(RecordingOutcome.Truncated, scrambled: true);
        Recording same = Ended(RecordingOutcome.Truncated, scrambled: true);

        before.Caption(CaptionState.Ready, 3, Later);
        before.Descrambled(Later.AddTicks(10));
        same.Caption(CaptionState.Ready, 3, Later);
        same.Descrambled(Later);

        Assert.True(before.AwaitsCaptions);
        Assert.False(same.AwaitsCaptions);
    }

    [Fact]
    public void ARecordingStartsWithItsCaptionsWaiting()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(CaptionState.Pending, recording.CaptionState);
        Assert.Null(recording.CaptionsMadeAt);
        Assert.Null(recording.CaptionPictures);
        Assert.Equal(0, recording.CaptionAttempts);
    }

    [Fact]
    public void BrPd016CaptionsAreNeverTakenWhileTheRecordingIsBeingWritten()
        => Assert.Throws<InvalidOperationException>(
            () => RecordingFactory.Started().Caption(CaptionState.Ready, 3, Later));

    [Fact]
    public void CaptionsThatAreReadySayHowManyChangesTheyKeepAndWhen()
    {
        Recording recording = Ended();

        recording.Caption(CaptionState.Ready, 455, Later);

        Assert.Equal(CaptionState.Ready, recording.CaptionState);
        Assert.Equal(455, recording.CaptionPictures);
        Assert.Equal(Later, recording.CaptionsMadeAt);
        Assert.Equal(0, recording.CaptionAttempts);
    }

    [Theory]
    [InlineData(CaptionState.Ready, null)]
    [InlineData(CaptionState.Ready, 0)]
    [InlineData(CaptionState.Absent, 1)]
    [InlineData(CaptionState.Failed, 1)]
    [InlineData(CaptionState.Pending, 1)]
    public void ACountThatDoesNotFitTheStateIsRefused(CaptionState state, int? pictures)
        => Assert.ThrowsAny<ArgumentException>(() => Ended().Caption(state, pictures, Later));

    [Fact]
    public void ACaptionStateTheLedgerDoesNotHoldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => Ended().Caption((CaptionState)99, null, Later));

    [Fact]
    public void BrPd016FailuresInARowAreCountedAndAnAnswerStartsTheCountAgain()
    {
        Recording recording = Ended();

        recording.Caption(CaptionState.Failed, null, Later);
        recording.Caption(CaptionState.Failed, null, Later.AddMinutes(5));

        Assert.Equal(CaptionState.Failed, recording.CaptionState);
        Assert.Equal(2, recording.CaptionAttempts);
        Assert.Equal(Later.AddMinutes(5), recording.CaptionsMadeAt);

        recording.Caption(CaptionState.Absent, null, Later.AddMinutes(10));

        Assert.Equal(0, recording.CaptionAttempts);
        Assert.Null(recording.CaptionPictures);
    }

    [Fact]
    public void PuttingTheCaptionsBackToWaitingForgetsWhenTheyWereTakenAndHowManyThereWere()
    {
        Recording recording = Ended();
        recording.Caption(CaptionState.Ready, 12, Later);

        recording.Caption(CaptionState.Pending, null, Later.AddHours(1));

        Assert.Equal(CaptionState.Pending, recording.CaptionState);
        Assert.Null(recording.CaptionsMadeAt);
        Assert.Null(recording.CaptionPictures);
    }

    [Fact]
    public void CaptionsTakenBeforeTheRecordingBeganAreRefused()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => Ended().Caption(CaptionState.Absent, null, RecordingFactory.Now.AddDays(-1))).ParamName);

    [Fact]
    public void ARecordingRehydratedWithCaptionsTakenWhileItWasBeingWrittenIsRefused()
    {
        RecordingId id = RecordingId.New();

        Assert.Equal("captionState", Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            null,
            null,
            RecordingFactory.Now,
            null,
            null,
            0,
            0,
            [],
            RecordingFactory.Now,
            RecordingFactory.Now.AddHours(1),
            RecordingFactory.Now.AddHours(1),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            null,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            Carina.Domain.Reservations.BroadcastGroupRole.Standalone,
            captionState: CaptionState.Absent,
            captionsMadeAt: Later)).ParamName);
    }

    [Fact(DisplayName = "what a second session counts is added to what the first counted")]
    public void WhatASecondSessionCountsIsAddedToWhatTheFirstCounted()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(5, 1_500), recording.Counters);
        Assert.Equal(25, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
        Assert.Equal(DropCounters.Counted(3, 1_000), recording.Carried.Counters);
        Assert.Equal(20, recording.Carried.ScrambledPackets);
        Assert.Equal(1, recording.Carried.Overflows);
        Assert.Equal(Reopened, recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "the same session read again is counted once")]
    public void TheSameSessionReadAgainIsCountedOnce()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);
        recording.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 6, 2, Began.AddMinutes(13), Reopened);

        Assert.Equal(DropCounters.Counted(7, 1_900), recording.Counters);
        Assert.Equal(26, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
    }

    [Fact(DisplayName = "a third session is added to both before it")]
    public void AThirdSessionIsAddedToBothBeforeIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);
        recording.Measure(
            DropCounters.Counted(1, 300),
            DropTimeline.Unlocated,
            0,
            0,
            Began.AddMinutes(22),
            Began.AddMinutes(20));

        Assert.Equal(DropCounters.Counted(6, 1_800), recording.Counters);
        Assert.Equal(25, recording.ScrambledPackets);
        Assert.Equal(3, recording.EovfCount);
        Assert.Equal(DropCounters.Counted(5, 1_500), recording.Carried.Counters);
    }

    [Fact(DisplayName = "the losses a later session places are placed after the ones before it, on the first session's clock")]
    public void TheLossesALaterSessionPlacesArePlacedAfterTheOnesBeforeIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(
            DropCounters.Counted(3, 1_000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            0,
            0,
            Began.AddMinutes(9),
            Began);
        recording.Measure(
            DropCounters.Counted(2, 500),
            DropTimeline.Rehydrate(900_000 + (602 * DropTimeline.TicksPerSecond), [new DropBucket(5, 2, 0)], []),
            0,
            0,
            Began.AddMinutes(12),
            Reopened);

        Assert.Equal(900_000, recording.Positions.AnchorPcr);
        Assert.Equal([new DropBucket(12, 3, 0), new DropBucket(607, 2, 0)], recording.Positions.Buckets);
        Assert.Equal([new DropBucket(12, 3, 0)], recording.Carried.Positions.Buckets);
    }

    [Fact(DisplayName = "a reading that names no session is the session already being counted")]
    public void AReadingThatNamesNoSessionIsTheSessionAlreadyBeingCounted()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);
        recording.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 6, 2, Began.AddMinutes(13));

        Assert.Equal(DropCounters.Counted(7, 1_900), recording.Counters);
        Assert.Equal(Reopened, recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "a recording counted before any session was named takes the first one named as the one it was counting")]
    public void ARecordingCountedBeforeAnySessionWasNamedTakesTheFirstOneNamedAsTheOneItWasCounting()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9));
        recording.Measure(DropCounters.Counted(4, 1_200), DropTimeline.Unlocated, 21, 1, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(4, 1_200), recording.Counters);
        Assert.Equal(DropCounters.Unmeasured, recording.Carried.Counters);
        Assert.Equal(Reopened, recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "a later session nothing counted leaves what was counted before it")]
    public void ALaterSessionNothingCountedLeavesWhatWasCountedBeforeIt()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(3, 1_000), recording.Counters);
        Assert.Equal(20, recording.ScrambledPackets);
        Assert.Equal(1, recording.EovfCount);
    }

    [Fact(DisplayName = "a first session nothing counted does not make what the next one counted uncounted")]
    public void AFirstSessionNothingCountedDoesNotMakeWhatTheNextOneCountedUncounted()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Began.AddMinutes(9), Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 0, Began.AddMinutes(12), Reopened);

        Assert.Equal(DropCounters.Counted(2, 500), recording.Counters);
        Assert.Equal(5, recording.ScrambledPackets);
        Assert.Equal(DropCounters.Unmeasured, recording.Carried.Counters);
        Assert.Null(recording.Carried.ScrambledPackets);
    }

    [Fact(DisplayName = "opening times that differ by less than a millisecond are one session")]
    public void OpeningTimesThatDifferByLessThanAMillisecondAreOneSession()
    {
        Recording recording = RecordingFactory.Started();
        DateTime opened = Reopened.AddTicks(1_234_567);

        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 0, 0, Began.AddMinutes(11), opened);
        recording.Measure(
            DropCounters.Counted(4, 1_200),
            DropTimeline.Unlocated,
            0,
            0,
            Began.AddMinutes(12),
            opened.AddTicks(-7));

        Assert.Equal(DropCounters.Counted(4, 1_200), recording.Counters);
        Assert.Equal(Reopened.AddMilliseconds(123), recording.CountedSessionOpenedAt);
    }

    [Fact(DisplayName = "what was carried and which session is being counted come back as they were written")]
    public void WhatWasCarriedAndWhichSessionIsBeingCountedComeBackAsTheyWereWritten()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(
            DropCounters.Counted(3, 1_000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            20,
            1,
            Began.AddMinutes(9),
            Began);
        recording.Measure(DropCounters.Counted(2, 500), DropTimeline.Unlocated, 5, 2, Began.AddMinutes(12), Reopened);

        Recording read = Reread(recording, recording.Carried, recording.CountedSessionOpenedAt);
        read.Measure(DropCounters.Counted(4, 900), DropTimeline.Unlocated, 6, 2, Began.AddMinutes(13), Reopened);

        Assert.Equal(DropCounters.Counted(7, 1_900), read.Counters);
        Assert.Equal(26, read.ScrambledPackets);
        Assert.Equal([new DropBucket(12, 3, 0)], read.Carried.Positions.Buckets);
    }

    [Fact(DisplayName = "a recording does not carry more than it counts")]
    public void ARecordingDoesNotCarryMoreThanItCounts()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(3, 1_000), DropTimeline.Unlocated, 20, 1, Began.AddMinutes(9), Began);

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Reread(
            recording,
            new CarriedCount(DropCounters.Counted(4, 1_000), DropTimeline.Unlocated, 20, 1),
            Began));

        Assert.Equal("carried", refusal.ParamName);
    }

    [Fact(DisplayName = "a carried count is not negative")]
    public void ACarriedCountIsNotNegative()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CarriedCount(DropCounters.Unmeasured, DropTimeline.Unlocated, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CarriedCount(DropCounters.Unmeasured, DropTimeline.Unlocated, null, -1));
    }

    private static Recording Reread(Recording recording, CarriedCount carried, DateTime? countedSessionOpenedAt)
        => Recording.Rehydrate(
            recording.Id,
            recording.ReservationId,
            recording.Programme,
            recording.OutputRoot,
            recording.FileName,
            recording.FileSizeObserved,
            recording.ObservedAt,
            recording.StartedAtActual,
            recording.StoppedAtActual,
            recording.AbortedAt,
            recording.WrittenDurationMs,
            recording.ResumeCount,
            recording.Interruptions,
            recording.ExpectedWindowStart,
            recording.ExpectedWindowEnd,
            recording.PromisedWindowEnd,
            recording.Outcome,
            recording.OutcomeDetail,
            recording.Counters,
            recording.Positions,
            recording.ScrambledPackets,
            recording.EovfCount,
            recording.MeasuredUpdatedAt,
            recording.TunerDeviceId,
            recording.ThumbnailState,
            RecordingFactory.Snapshot(),
            recording.BroadcastGroupKey,
            recording.BroadcastGroupRole,
            carried: carried,
            countedSessionOpenedAt: countedSessionOpenedAt);

    [Fact(DisplayName = "a recording carried over arrives as one recorded in full, over the times the source system recorded it")]
    public void ARecordingCarriedOverArrivesRecordedInFullOverItsOwnTimes()
    {
        Recording carried = Carried();

        Assert.Equal(RecordingOutcome.Complete, carried.Outcome);
        Assert.False(carried.IsInFlight);
        Assert.Equal(BeganAtTheSource, carried.StartedAtActual);
        Assert.Equal(EndedAtTheSource, carried.StoppedAtActual);
        Assert.Equal(BeganAtTheSource, carried.ExpectedWindowStart);
        Assert.Equal(EndedAtTheSource, carried.ExpectedWindowEnd);
        Assert.Equal(EndedAtTheSource, carried.PromisedWindowEnd);
        Assert.Equal((long)(EndedAtTheSource - BeganAtTheSource).TotalMilliseconds, carried.WrittenDurationMs);
        Assert.Equal(EndedAtTheSource, carried.AbortedAt);
    }

    [Fact(DisplayName = "BR-MD-005: a recording carried over holds no reservation, no tuner and no mark of where it came from")]
    public void ARecordingCarriedOverHoldsNoMemoryOfWhereItCameFrom()
    {
        Recording carried = Carried();

        Assert.Null(carried.ReservationId);
        Assert.Null(carried.TunerDeviceId);
        Assert.Equal(BroadcastGroupRole.Standalone, carried.BroadcastGroupRole);
        Assert.Equal(0, carried.ResumeCount);
        Assert.Empty(carried.Interruptions);
    }

    [Fact(DisplayName = "BR-MD-006: a recording carried over is unmeasured and waits for its picture to be made from the file")]
    public void ARecordingCarriedOverIsUnmeasuredAndAwaitsItsPicture()
    {
        Recording carried = Carried();

        Assert.False(carried.CcMeasured);
        Assert.Null(carried.CcDroppedPackets);
        Assert.Null(carried.CcTotalPackets);
        Assert.Null(carried.MeasuredUpdatedAt);
        Assert.Equal(ThumbnailState.Pending, carried.ThumbnailState);
        Assert.True(carried.AwaitsAThumbnail);
    }

    [Fact(DisplayName = "a recording carried over keeps the size its file was seen at, the moment it was carried, and the source system's title alone")]
    public void ARecordingCarriedOverKeepsItsSizeAndTitle()
    {
        Recording carried = Carried();

        Assert.Equal(100, carried.FileSizeObserved);
        Assert.Equal(CarriedAt, carried.ObservedAt);
        Assert.Equal(Programme, carried.Programme);
        Assert.Equal("a programme", carried.SnapshotName);
        Assert.Equal(string.Empty, carried.SnapshotSummary);
        Assert.Equal(string.Empty, carried.SnapshotExtended);
        Assert.Empty(carried.SnapshotGenres);
        Assert.Equal(AudioMode.Undetermined, carried.SnapshotAudio);
        Assert.Equal(ProgrammeSnapshot.SoundsUnannounced, carried.SnapshotSounds);
    }

    [Fact(DisplayName = "a recording carried over lives in the new root under the name that carries its own id")]
    public void ARecordingCarriedOverLivesUnderItsOwnName()
    {
        RecordingId id = RecordingId.New();
        RecordingFileName name = RecordingFileName.For(id, ".m2ts");

        Recording carried = Recording.CarriedOver(
            id,
            Programme,
            "a programme",
            new OutputRoot("carried"),
            name,
            100,
            EndedAtTheSource,
            CarriedAt);

        Assert.Equal(id, carried.Id);
        Assert.Equal(new OutputRoot("carried"), carried.OutputRoot);
        Assert.Equal(name, carried.FileName);
    }

    [Fact(DisplayName = "a recording that ends on the instant it starts cannot be carried over as one")]
    public void ARecordingThatEndsAsItStartsCannotBeCarriedOver()
    {
        RecordingId id = RecordingId.New();

        Assert.Throws<ArgumentException>(() => Recording.CarriedOver(
            id,
            Programme,
            "a programme",
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            100,
            BeganAtTheSource,
            CarriedAt));
    }

    private static Recording Carried()
    {
        RecordingId id = RecordingId.New();

        return Recording.CarriedOver(
            id,
            Programme,
            "a programme",
            new OutputRoot("carried"),
            RecordingFileName.For(id, ".m2ts"),
            100,
            EndedAtTheSource,
            CarriedAt);
    }

    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record of its data broadcast due in any way")]
    public void ARecordingStillBeingWrittenHasNothingDue()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.AwaitsItsDataBroadcast);
        Assert.False(recording.DataBroadcastNeverCameDue);
        Assert.False(recording.DataBroadcastIsDueAgain);
    }

    [Theory(DisplayName = "BR-BS-001: the data broadcast of a recording that has ended is awaited")]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void TheDataBroadcastOfARecordingThatHasEndedIsAwaited(RecordingOutcome outcome)
    {
        Recording recording = Ended(outcome);

        Assert.True(recording.AwaitsItsDataBroadcast);
        Assert.False(recording.DataBroadcastNeverCameDue);
        Assert.False(recording.DataBroadcastIsDueAgain);
    }

    [Theory(DisplayName = "BR-BS-001: a record taken, made or missing, is awaited no longer")]
    [InlineData(0)]
    [InlineData(36)]
    public void ARecordTakenIsNoLongerAwaited(int modules)
    {
        Recording recording = Ended(RecordingOutcome.Truncated);

        recording.DataBroadcastTaken(modules, Later);

        Assert.False(recording.AwaitsItsDataBroadcast);
        Assert.False(recording.DataBroadcastIsDueAgain);
    }

    [Fact(DisplayName = "BR-BS-001: a record that failed is due again until it has failed three times in all")]
    public void ARecordThatFailedIsDueAgainUntilTheTriesRunOut()
    {
        Recording recording = Ended(RecordingOutcome.Truncated);

        for (int failed = 1; failed < DataBroadcastProgress.TriesAtMost; failed++)
        {
            recording.DataBroadcastFailed(Later.AddMinutes(failed));

            Assert.False(recording.AwaitsItsDataBroadcast);
            Assert.True(recording.DataBroadcastIsDueAgain);

            recording.DataBroadcastAgain();

            Assert.True(recording.AwaitsItsDataBroadcast);
        }

        recording.DataBroadcastFailed(Later.AddHours(1));

        Assert.False(recording.DataBroadcastIsDueAgain);
        Assert.False(recording.AwaitsItsDataBroadcast);
    }

    [Fact(DisplayName = "BR-BS-001: a recording that ended under a process that did not know the record has it never come due, until it is put to coming")]
    public void ARecordingThatEndedWithNoRecordDueNeverCameDue()
    {
        Recording recording = EndedWithNoRecordDue();

        Assert.True(recording.DataBroadcastNeverCameDue);
        Assert.False(recording.AwaitsItsDataBroadcast);

        recording.DataBroadcastAgain();

        Assert.False(recording.DataBroadcastNeverCameDue);
        Assert.True(recording.AwaitsItsDataBroadcast);
    }

    private static Recording EndedWithNoRecordDue()
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            1_000,
            EndsInAnHour,
            RecordingFactory.Now,
            EndsInAnHour,
            EndsInAnHour,
            0,
            0,
            [],
            RecordingFactory.Now,
            EndsInAnHour,
            EndsInAnHour,
            RecordingOutcome.Truncated,
            [RecordingFactory.Fault()],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            new TunerDeviceId("pt3-0"),
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            Carina.Domain.Reservations.BroadcastGroupRole.Standalone,
            dataBroadcast: DataBroadcastProgress.NotYet);
    }

    [Fact(DisplayName = "BR-BS-001: a recording still being written has no record of its data broadcast due")]
    public void ARecordingStillBeingWrittenHasNoRecordDue()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal((DataBroadcastState.None, (DateTime?)null, (int?)null, 0), Of(recording));
        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastTaken(3, Later));
        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastFailed(Later));
        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastAgain());
    }

    [Theory(DisplayName = "BR-BS-001: a recording that ends has the record of its data broadcast coming, however it ended")]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void ARecordingThatEndsHasItsRecordComing(RecordingOutcome outcome)
    {
        Recording recording = Ended(outcome);

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact(DisplayName = "BR-BS-001: a record taken with modules is made and says how many and when")]
    public void ARecordTakenWithModulesIsMadeAndSaysHowManyAndWhen()
    {
        Recording recording = Ended();

        recording.DataBroadcastTaken(36, Later);

        Assert.Equal((DataBroadcastState.Made, (DateTime?)Later, (int?)36, 0), Of(recording));
        Assert.Equal(recording.DataBroadcast, new DataBroadcastProgress(DataBroadcastState.Made, 0, 36));
    }

    [Fact(DisplayName = "BR-BS-001: a record taken with no modules is missing")]
    public void ARecordTakenWithNoModulesIsMissing()
    {
        Recording recording = Ended();

        recording.DataBroadcastTaken(0, Later);

        Assert.Equal((DataBroadcastState.Missing, (DateTime?)Later, (int?)null, 0), Of(recording));
    }

    [Fact(DisplayName = "BR-BS-001: failures are counted in all, three tries and no more, and a record made forgets them")]
    public void FailuresAreCountedInAllThreeTriesAndNoMore()
    {
        Recording recording = Ended();

        recording.DataBroadcastFailed(Later);
        recording.DataBroadcastAgain();
        recording.DataBroadcastFailed(Later.AddMinutes(5));

        Assert.Equal((DataBroadcastState.Failed, (DateTime?)Later.AddMinutes(5), (int?)null, 2), Of(recording));

        recording.DataBroadcastAgain();

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 2), Of(recording));

        recording.DataBroadcastFailed(Later.AddMinutes(10));

        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastAgain());
        Assert.Equal(3, recording.DataBroadcastAttempts);
    }

    [Fact(DisplayName = "BR-BS-001: a record that is made and no longer kept comes again")]
    public void ARecordMadeAndNoLongerKeptComesAgain()
    {
        Recording recording = Ended();
        recording.DataBroadcastTaken(2, Later);

        recording.DataBroadcastAgain();

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact(DisplayName = "BR-BS-001: a recording that ended with no record due, under a process that did not know it, has it coming")]
    public void ARecordingThatEndedWithNoRecordDueHasItComing()
    {
        Recording recording = Rehydrated(RecordingOutcome.Failed, DataBroadcastProgress.NotYet, null);

        recording.DataBroadcastAgain();

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact]
    public void ARecordThatIsMissingDoesNotComeAgainUnlessTheRecordingIsDescrambled()
    {
        Recording recording = Ended();
        recording.DataBroadcastTaken(0, Later);

        Assert.Throws<InvalidOperationException>(() => recording.DataBroadcastAgain());
    }

    [Theory(DisplayName = "BR-BS-001: a record taken, found missing or failed is taken again once the recording is descrambled")]
    [InlineData(5)]
    [InlineData(0)]
    [InlineData(-1)]
    public void ARecordIsTakenAgainOnceTheRecordingIsDescrambled(int modules)
    {
        Recording recording = Ended(RecordingOutcome.Truncated, scrambled: true);
        Settle(recording, modules);

        recording.Descrambled(Later.AddDays(1));

        Assert.Equal(DataBroadcastState.Coming, recording.DataBroadcastState);
        Assert.Null(recording.DataBroadcastMadeAt);
        Assert.Null(recording.DataBroadcastModules);
    }

    [Fact]
    public void ARecordStillComingStaysComingWhenTheRecordingIsDescrambled()
    {
        Recording recording = Ended(RecordingOutcome.Truncated, scrambled: true);

        recording.Descrambled(Later);

        Assert.Equal((DataBroadcastState.Coming, (DateTime?)null, (int?)null, 0), Of(recording));
    }

    [Fact]
    public void ADataBroadcastTakenBeforeTheRecordingBeganIsRefused()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => Ended().DataBroadcastTaken(1, RecordingFactory.Now.AddDays(-1))).ParamName);

    [Fact]
    public void ARecordingRehydratedWithNothingSaidAboutItsDataBroadcastHasItComingOnceItHasEnded()
    {
        Assert.Equal(DataBroadcastState.None, Rehydrated(null, null, null).DataBroadcastState);
        Assert.Equal(DataBroadcastState.Coming, Rehydrated(RecordingOutcome.Failed, null, null).DataBroadcastState);
    }

    [Fact(DisplayName = "BR-BS-001: a recording rehydrated with a record taken while it was being written is refused")]
    public void ARecordingRehydratedWithARecordTakenWhileItWasBeingWrittenIsRefused()
        => Assert.Equal(
            "outcome",
            Assert.Throws<ArgumentException>(() => Rehydrated(null, new DataBroadcastProgress(DataBroadcastState.Coming, 0, null), null)).ParamName);

    [Theory]
    [InlineData(DataBroadcastState.Made, false)]
    [InlineData(DataBroadcastState.Coming, true)]
    public void ARecordingRehydratedWithAMomentThatDoesNotFitItsRecordIsRefused(DataBroadcastState state, bool saysWhen)
        => Assert.Equal(
            "madeAt",
            Assert.Throws<ArgumentException>(() => Rehydrated(
                RecordingOutcome.Failed,
                new DataBroadcastProgress(state, 0, state is DataBroadcastState.Made ? 2 : null),
                saysWhen ? Later : null)).ParamName);

    private static void Settle(Recording recording, int modules)
    {
        if (modules < 0)
        {
            recording.DataBroadcastFailed(Later);

            return;
        }

        recording.DataBroadcastTaken(modules, Later);
    }

    private static Recording Ended(RecordingOutcome outcome = RecordingOutcome.Truncated, bool scrambled = false)
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(EndsInAnHour);
        recording.Note(RecordingFactory.Fault(scrambled ? RecordingFault.ScramblingUnresolved : RecordingFault.DriverLost));
        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000, EndsInAnHour);

        return recording;
    }

    private static Recording Rehydrated(RecordingOutcome? outcome, DataBroadcastProgress? progress, DateTime? madeAt)
    {
        RecordingId id = RecordingId.New();
        bool ended = outcome is not null;

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".ts"),
            ended ? 0 : null,
            ended ? EndsInAnHour : null,
            RecordingFactory.Now,
            ended ? EndsInAnHour : null,
            null,
            0,
            0,
            [],
            RecordingFactory.Now,
            EndsInAnHour,
            EndsInAnHour,
            outcome,
            ended ? [RecordingFactory.Fault()] : [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            dataBroadcast: progress,
            dataBroadcastMadeAt: madeAt);
    }

    private static (DataBroadcastState, DateTime?, int?, int) Of(Recording recording)
        => (recording.DataBroadcastState, recording.DataBroadcastMadeAt, recording.DataBroadcastModules, recording.DataBroadcastAttempts);

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void ARecordingThatEndedWithItsScramblingUnresolvedIsLeftScrambled(RecordingOutcome outcome)
    {
        Recording recording = Scrambled(outcome);

        Assert.True(recording.LeftScrambled);
        Assert.Null(recording.DescrambledAt);
    }

    [Fact]
    public void ARecordingThatNamesNoScramblingIsNotLeftScrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(EndedAfterAnHour);
        recording.Settle(RecordingOutcome.Complete, 1_200_000, EndedAfterAnHour);

        Assert.False(recording.LeftScrambled);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotYetLeftScrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));

        Assert.False(recording.LeftScrambled);
    }

    [Theory]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void DescramblingLiftsWhatWasLeftScrambledAndSaysWhen(RecordingOutcome outcome)
    {
        Recording recording = Scrambled(outcome);

        recording.Descrambled(TwoDaysAfterItEnded);

        Assert.False(recording.LeftScrambled);
        Assert.Equal(TwoDaysAfterItEnded, recording.DescrambledAt);
    }

    [Fact]
    public void DescramblingKeepsTheOutcomeAndTheClassificationItEndedWith()
    {
        Recording recording = Scrambled(RecordingOutcome.Complete);

        recording.Descrambled(TwoDaysAfterItEnded);

        Assert.Equal(RecordingOutcome.Complete, recording.Outcome);
        Assert.Contains(
            recording.OutcomeDetail,
            detail => detail.Fault is RecordingFault.ScramblingUnresolved);
    }

    [Fact]
    public void ARecordingThatWasNeverLeftScrambledIsNotDescrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, EndedAfterAnHour);

        Assert.Throws<InvalidOperationException>(() => recording.Descrambled(TwoDaysAfterItEnded));
        Assert.Null(recording.DescrambledAt);
    }

    [Fact]
    public void ARecordingStillBeingWrittenIsNotDescrambled()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));

        Assert.Throws<InvalidOperationException>(() => recording.Descrambled(TwoDaysAfterItEnded));
    }

    [Fact]
    public void ARecordingIsDescrambledOnce()
    {
        Recording recording = Scrambled(RecordingOutcome.Complete);
        recording.Descrambled(TwoDaysAfterItEnded);

        Assert.Throws<InvalidOperationException>(() => recording.Descrambled(TwoDaysAfterItEnded.AddDays(1)));
        Assert.Equal(TwoDaysAfterItEnded, recording.DescrambledAt);
    }

    [Fact]
    public void ARecordingIsNotDescrambledBeforeItEnded()
    {
        Recording recording = Scrambled(RecordingOutcome.Complete);

        Assert.Throws<ArgumentException>(() => recording.Descrambled(EndedAfterAnHour.AddSeconds(-1)));
        Assert.Throws<ArgumentException>(
            () => recording.Descrambled(DateTime.SpecifyKind(TwoDaysAfterItEnded, DateTimeKind.Local)));
        Assert.True(recording.LeftScrambled);
    }

    [Fact]
    public void ARowReadBackKeepsWhenItWasDescrambled()
    {
        Recording held = Scrambled(RecordingOutcome.Complete);

        Recording again = Again(held, TwoDaysAfterItEnded);

        Assert.Equal(TwoDaysAfterItEnded, again.DescrambledAt);
        Assert.False(again.LeftScrambled);
    }

    [Fact]
    public void ARowCannotSayItWasDescrambledUnlessItEndedLeftScrambledBeforeThat()
    {
        Recording unscrambled = RecordingFactory.Started();
        unscrambled.Note(RecordingFactory.Fault());
        unscrambled.Settle(RecordingOutcome.Truncated, 1_200_000, EndedAfterAnHour);

        Assert.Throws<ArgumentException>(() => Again(unscrambled, TwoDaysAfterItEnded));
        Assert.Throws<ArgumentException>(() => Again(RecordingFactory.Started(), TwoDaysAfterItEnded));
        Assert.Throws<ArgumentException>(() => Again(Scrambled(RecordingOutcome.Complete), EndedAfterAnHour.AddSeconds(-1)));
    }

    private static Recording Scrambled(RecordingOutcome outcome)
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));

        if (outcome is RecordingOutcome.Complete)
        {
            recording.Abort(EndedAfterAnHour);
        }

        recording.Settle(outcome, 1_200_000, EndedAfterAnHour);

        return recording;
    }

    private static Recording Again(Recording held, DateTime? descrambledAt)
        => Recording.Rehydrate(
            held.Id,
            held.ReservationId,
            held.Programme,
            held.OutputRoot,
            held.FileName,
            held.FileSizeObserved,
            held.ObservedAt,
            held.StartedAtActual,
            held.StoppedAtActual,
            held.AbortedAt,
            held.WrittenDurationMs,
            held.ResumeCount,
            held.Interruptions,
            held.ExpectedWindowStart,
            held.ExpectedWindowEnd,
            held.PromisedWindowEnd,
            held.Outcome,
            held.OutcomeDetail,
            held.Counters,
            held.Positions,
            held.ScrambledPackets,
            held.EovfCount,
            held.MeasuredUpdatedAt,
            held.TunerDeviceId,
            held.ThumbnailState,
            RecordingFactory.Snapshot(),
            held.BroadcastGroupKey,
            held.BroadcastGroupRole,
            held.ThumbnailFault,
            held.LeftBehindAt,
            held.FilesLeftBehind,
            held.EncodeWhenRecorded,
            descrambledAt);

    private static void Take(Recording recording, SessionCounters counters)
    {
        RecordingSessionDto sent = RecordingSessionDto.Of(
            Driver,
            new SessionSnapshot(
                SessionId.Parse("rec-1"),
                SessionPurpose.Recording,
                "adapter0",
                SessionState.Active,
                Now)
            {
                Counters = counters,
            });

        recording.Measure(
            sent.CcMeasured
                ? DropCounters.Counted(sent.CcDropped ?? 0, sent.CcTotal ?? 0)
                : DropCounters.Unmeasured,
            sent.Positions is not { } positions
                ? DropTimeline.Unlocated
                : DropTimeline.Rehydrate(
                    positions.AnchorPcr,
                    [
                        .. positions.Buckets.Select(bucket =>
                            new DropBucket(bucket.Second, bucket.Continuity, bucket.Scrambled)),
                    ],
                    [
                        .. positions.Reanchors.Select(reanchor =>
                            new PcrReanchor(reanchor.Second, reanchor.Before, reanchor.After)),
                    ]),
            sent.ScrambledPackets,
            sent.EovfCount,
            Now);
    }

    [Fact]
    public void ACountWithNothingToPlaceIsTakenAsItStands()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(
                Packets: 50_000,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(900_000, [], [])));

        Assert.True(recording.CcMeasured);
        Assert.Equal(0, recording.CcDroppedPackets);
        Assert.True(recording.Positions.Located);
        Assert.Empty(recording.Positions.Buckets);
    }

    [Fact]
    public void ACountWhoseSecondsAddUpToItIsTakenAsItStands()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(
                Packets: 50_000,
                Drops: 9,
                ScrambledPackets: 2,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(
                    900_000,
                    [new DropBucketDto(0, 7, 0), new DropBucketDto(11, 2, 2)],
                    [new PcrReanchorDto(4, 123, 456)])));

        Assert.Equal(9, recording.CcDroppedPackets);
        Assert.Equal(9, recording.Positions.Continuity);
        Assert.Equal(2, recording.Positions.Scrambled);
        Assert.Equal([0, 11], recording.Positions.Buckets.Select(bucket => bucket.Second));
        Assert.Single(recording.Positions.Reanchors);
    }

    [Fact]
    public void ACountThatWasReadWhileItWasStillMovingIsRefusedRatherThanStored()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Take(
            recording,
            new SessionCounters(
                Packets: 50_000,
                Drops: 7,
                CcMeasured: true,
                ScrambleMeasured: true,
                Positions: new DropPositionsDto(900_000, [new DropBucketDto(4, 9, 0)], []))));

        Assert.Equal("positions", refusal.ParamName);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void APositionOnACountNobodyTookNeverReachesTheLedgerAtAll()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(recording, new SessionCounters(Positions: new DropPositionsDto(900_000, [], [])));

        Assert.False(recording.CcMeasured);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void ACountFromADriverThatNeverSawTheClockKeepsItsNumbersAndNoPosition()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(Packets: 50_000, Drops: 7, CcMeasured: true));

        Assert.True(recording.CcMeasured);
        Assert.Equal(7, recording.CcDroppedPackets);
        Assert.False(recording.Positions.Located);
    }

    [Theory]
    [InlineData(40, 117)]
    [InlineData(20, 304)]
    [InlineData(1, 1_000_000)]
    public void ARecordingThatLostMoreThanItReadIsStillATallyTheLedgerTakes(long read, long lost)
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(
                Packets: read,
                Drops: lost,
                CcMeasured: true,
                ScrambleMeasured: true));

        Assert.Equal(lost, recording.CcDroppedPackets);
        Assert.Equal(read + lost, recording.CcTotalPackets);
    }

    [Fact]
    public void TheTotalIsWhatTheStreamShouldHaveCarriedRatherThanWhatArrived()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("adapter0"));

        Take(
            recording,
            new SessionCounters(Packets: 900, Drops: 100, CcMeasured: true, ScrambleMeasured: true));

        Assert.Equal(1000, recording.CcTotalPackets);
        Assert.Equal(100, recording.CcDroppedPackets);
    }

    [Fact(DisplayName = "BR-KS-001: a recording nobody asked to stop is over from the instant its window closes, and not before")]
    public void ARecordingIsOverFromTheInstantItsWindowCloses()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.IsOver(recording.ExpectedWindowEnd.AddTicks(-1)));
        Assert.True(recording.IsOver(recording.ExpectedWindowEnd));
        Assert.True(recording.IsOver(recording.ExpectedWindowEnd.AddHours(1)));
    }

    [Fact(DisplayName = "BR-KD-006: a recording this side asked to stop is over at once, whatever its window says")]
    public void ARecordingAskedToStopIsOverAtOnce()
    {
        Recording recording = RecordingFactory.Started();

        recording.Abort(Now);

        Assert.True(recording.IsOver(Now));
        Assert.True(recording.IsOver(recording.ExpectedWindowStart));
    }

    [Fact(DisplayName = "BR-KD-009: a recording that follows its programme later is over when the later end comes")]
    public void ARecordingThatFollowsItsProgrammeIsOverWhenTheLaterEndComes()
    {
        Recording recording = RecordingFactory.Started();
        DateTime promised = recording.ExpectedWindowEnd;

        recording.Extend(promised.AddMinutes(10));

        Assert.False(recording.IsOver(promised));
        Assert.True(recording.IsOver(promised.AddMinutes(10)));
    }

    [Fact(DisplayName = "BR-KD-006: a recording is due to be stopped once its window has closed, until this side has asked")]
    public void ARecordingIsDueToBeStoppedOnceItsWindowClosesUntilAsked()
    {
        Recording recording = RecordingFactory.Started();
        DateTime closes = recording.ExpectedWindowEnd;

        Assert.False(recording.IsDueToStop(closes.AddTicks(-1)));
        Assert.True(recording.IsDueToStop(closes));

        recording.Abort(closes);

        Assert.False(recording.IsDueToStop(closes));
        Assert.False(recording.IsDueToStop(closes.AddHours(1)));
    }

    [Fact(DisplayName = "BR-KD-006: a recording stopped before its window closed is over and is not stopped a second time")]
    public void ARecordingStoppedEarlyIsOverAndNotStoppedAgain()
    {
        Recording recording = RecordingFactory.Started();

        recording.Abort(Now);

        Assert.True(recording.IsOver(Now));
        Assert.False(recording.IsDueToStop(recording.ExpectedWindowEnd));
    }

    [Fact(DisplayName = "BR-KD-009: a recording made for a reservation follows its programme until this side asks it to stop")]
    public void ARecordingMadeForAReservationFollowsItsProgrammeUntilAskedToStop()
    {
        Recording recording = RecordingFactory.Started(reservationId: ReservationId.New());

        Assert.True(recording.FollowsItsProgramme);
        Assert.True(recording.IsOver(recording.ExpectedWindowEnd));
        Assert.True(recording.FollowsItsProgramme);

        recording.Abort(Now);

        Assert.False(recording.FollowsItsProgramme);
    }

    [Fact(DisplayName = "BR-KD-009: a recording no reservation asked for has no margin to follow a programme with")]
    public void ARecordingNoReservationAskedForDoesNotFollowItsProgramme()
        => Assert.False(RecordingFactory.Started().FollowsItsProgramme);

    [Fact]
    public void TheLengthsAreTheOnesTheFileSystemAndTheWireGive()
    {
        Assert.Equal(255, RecordingFileName.MaxLength);
        Assert.Equal(64, OutputRoot.MaxLength);
        Assert.Equal(64, TunerDeviceId.MaxLength);

        Assert.Equal(255, new RecordingFileName(new string('a', 255)).Value.Length);
        Assert.Throws<ArgumentException>(() => new RecordingFileName(new string('a', 256)));

        Assert.Equal(64, new OutputRoot(new string('a', 64)).Value.Length);
        Assert.Throws<ArgumentException>(() => new OutputRoot(new string('a', 65)));

        Assert.Equal(64, new TunerDeviceId(new string('a', 64)).Value.Length);
        Assert.Throws<ArgumentException>(() => new TunerDeviceId(new string('a', 65)));
    }

    [Fact]
    public void ASettledRecordingIsNotMeasuredAgainNineDaysLater()
    {
        Recording recording = Settled();

        Assert.Throws<InvalidOperationException>(() => recording.Measure(
            DropCounters.Counted(4, 900),
            DropTimeline.Unlocated,
            null,
            0,
            Now.AddDays(9)));
    }

    [Fact]
    public void ASettledRecordingTakesNoFurtherReason()
    {
        Recording recording = Settled();

        Assert.Throws<InvalidOperationException>(() => recording.Note(RecordingFactory.Fault()));
        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public void NothingCountsBackwardsWhenARecordingIsMeasured()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "scrambledPackets",
            Assert.Throws<ArgumentOutOfRangeException>(() => recording.Measure(
                DropCounters.Unmeasured,
                DropTimeline.Unlocated,
                -1,
                0,
                Now)).ParamName);
        Assert.Equal(
            "eovfCount",
            Assert.Throws<ArgumentOutOfRangeException>(() => recording.Measure(
                DropCounters.Unmeasured,
                DropTimeline.Unlocated,
                null,
                -1,
                Now)).ParamName);
    }

    [Fact]
    public void AFileIsNotSmallerThanEmptyWhenARecordingSettles()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now);

        Assert.Equal(
            "fileSizeObserved",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => recording.Settle(RecordingOutcome.Complete, -1, Now)).ParamName);
    }

    [Theory]
    [InlineData("writtenDurationMs")]
    [InlineData("resumeCount")]
    [InlineData("fileSizeObserved")]
    [InlineData("scrambledPackets")]
    [InlineData("eovfCount")]
    public void NothingCountsBackwardsOnARehydratedRecording(string parameter)
        => Assert.Equal(parameter, Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated(parameter)).ParamName);

    [Fact]
    public void ARoleTheLedgerDoesNotHoldIsRefused()
        => Assert.Equal(
            "broadcastGroupRole",
            Assert.Throws<ArgumentOutOfRangeException>(() => Rehydrated("broadcastGroupRole")).ParamName);

    private static Recording Rehydrated(string parameter)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            parameter is "fileSizeObserved" ? -1 : null,
            parameter is "fileSizeObserved" ? Now : null,
            Now,
            null,
            null,
            parameter is "writtenDurationMs" ? -1 : 0,
            parameter is "resumeCount" ? -1 : 0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            parameter is "scrambledPackets" ? -1 : null,
            parameter is "eovfCount" ? -1 : 0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            parameter is "broadcastGroupRole" ? (BroadcastGroupRole)99 : BroadcastGroupRole.Standalone);
    }

    [Fact]
    public void ARecordingOffersTheseChangesAndNothingElse()
    {
        string[] offered = Named(Declared(BindingFlags.Public | BindingFlags.Instance).Where(Changes));

        Assert.Equal(
            [
                "Abort", "Acquire", "Adopt", "Caption", "Conclude", "DataBroadcastAgain", "DataBroadcastFailed",
                "DataBroadcastTaken", "Descrambled", "Erased", "Extend", "FailOnAFullDisk", "Illustrate", "Interrupt", "Measure",
                "Missed", "Note", "NoteEndUndecided", "Prechecked", "ReachedItsEnd", "Resume", "Settle", "SettleOnWhatWasLeft",
                "Wrote",
            ],
            offered);
        Assert.Equal(offered.Length, Declared(BindingFlags.Public | BindingFlags.Instance).Count(Changes));
    }

    [Fact]
    public void ARecordingAnswersTheseQuestionsAndNothingElse()
        => Assert.Equal(
            ["Follows", "IsDueToStop", "IsOver", "KeepsCountingAt", "StoppedWritingAt"],
            Named(Declared(BindingFlags.Public | BindingFlags.Instance).Where(method => !Changes(method))));

    [Fact]
    public void AskingARecordingAQuestionMovesNothing()
    {
        foreach (Recording recording in new[] { RecordingFactory.Started(), SettledWithScramblingUnresolved() })
        {
            IReadOnlyDictionary<string, string> before = Read(recording);

            _ = recording.Follows(new RecordingGap(AnHourLater, AnHourLater.AddSeconds(3)));
            _ = recording.IsDueToStop(AnHourLater);
            _ = recording.IsOver(AnHourLater);
            _ = recording.KeepsCountingAt(AnHourLater);
            _ = recording.StoppedWritingAt(Now, AnHourLater);

            Assert.Equal(before, Read(recording));
        }
    }

    private static bool Changes(MethodInfo method) => method.ReturnType == typeof(void);

    private static string[] Named(IEnumerable<MethodInfo> methods)
        =>
        [
            .. methods
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal),
        ];

    [Fact]
    public void NothingChangesARecordingFromOutsideItsOwnMethods()
    {
        Assert.Empty(typeof(Recording)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod is { IsPublic: true })
            .Select(property => property.Name));
        Assert.Empty(typeof(Recording)
            .GetFields(BindingFlags.Public | BindingFlags.Instance)
            .Select(field => field.Name));
    }

    [Fact]
    public void TheOnlyThingsARecordingOffersWithoutOneAreTheThreeThatMakeOne()
        => Assert.Equal(
            ["Begin", "CarriedOver", "Rehydrate"],
            Declared(BindingFlags.Public | BindingFlags.Static)
                .Select(method => method.Name)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray());

    private static MethodInfo[] Declared(BindingFlags reaching)
        => [.. typeof(Recording)
            .GetMethods(reaching | BindingFlags.DeclaredOnly)
            .Where(method => !method.IsSpecialName)];

    [Fact]
    public void EveryOneOfThemButThePictureTheCaptionsTheDataBroadcastTheErasureAndTheDescramblingRefusesOnceTheRecordingHasEnded()
    {
        Recording recording = SettledWithScramblingUnresolved();

        Assert.Throws<InvalidOperationException>(() => recording.Abort(AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.Acquire(RecordingFactory.Tuner));
        Assert.Throws<InvalidOperationException>(() => recording.Extend(AnHourLater.AddHours(1)));
        Assert.Throws<InvalidOperationException>(
            () => recording.Interrupt(RecordingFault.DriverLost, AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.Measure(
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            AnHourLater));
        Assert.Throws<InvalidOperationException>(
            () => recording.Missed(new RecordingGap(AnHourLater, AnHourLater.AddSeconds(3))));
        Assert.Throws<InvalidOperationException>(() => recording.Note(RecordingFactory.Fault()));
        Assert.Throws<InvalidOperationException>(() => recording.Resume(AnHourLater));
        Assert.Throws<InvalidOperationException>(
            () => recording.Settle(RecordingOutcome.Complete, 1, AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.Wrote(TimeSpan.FromMinutes(1)));
        Assert.Throws<InvalidOperationException>(() => recording.FailOnAFullDisk(1, AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.SettleOnWhatWasLeft(false, 1, Bands, AnHourLater));
        Assert.Throws<InvalidOperationException>(
            () => recording.Conclude(1, ExpectedBitrate.Terrestrial, Bands, AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.ReachedItsEnd(null, AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.Adopt("pt3-4"));
        Assert.Throws<InvalidOperationException>(() => recording.NoteEndUndecided(AnHourLater));
        Assert.Throws<InvalidOperationException>(() => recording.Prechecked(
            DiskPrecheckVerdict.Of(DiskShortfall.NoRoomLeft, 1, 0, 1),
            AnHourLater));

        recording.Illustrate(ThumbnailState.Ready);
        recording.Caption(CaptionState.Ready, 4, AnHourLater);
        recording.DataBroadcastTaken(3, AnHourLater);
        recording.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", 1), AnHourLater);
        recording.Descrambled(AnHourLater);

        Assert.Equal(ThumbnailState.Ready, recording.ThumbnailState);
        Assert.Equal(CaptionState.Ready, recording.CaptionState);
        Assert.Equal(DataBroadcastState.Coming, recording.DataBroadcastState);
        Assert.Equal(AnHourLater, recording.LeftBehindAt);
        Assert.Equal(AnHourLater, recording.DescrambledAt);
    }

    [Fact]
    public void DescramblingMovesTheColumnThatSaysSoAndNoOthers()
    {
        Recording recording = SettledWithScramblingUnresolved();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Descrambled(AnHourLater);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(Recording.DescrambledAt)], moved);
    }

    [Fact]
    public void KeepingWhatAnErasureLeftBehindMovesTheColumnsThatSaySoAndNoOthers()
    {
        Recording recording = SettledWithScramblingUnresolved();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", 2), AnHourLater);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.Equal([nameof(Recording.FilesLeftBehind), nameof(Recording.LeftBehindAt)], moved);
    }

    [Theory]
    [InlineData(ThumbnailState.Ready, null)]
    [InlineData(ThumbnailState.Skipped, null)]
    [InlineData(ThumbnailState.Failed, ThumbnailFault.TimedOut)]
    public void DrawingThePictureMovesTheColumnsThatAreThePictureAndNoOthers(
        ThumbnailState state,
        ThumbnailFault? fault)
    {
        Recording recording = SettledWithScramblingUnresolved();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Illustrate(state, fault);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(moved);
        Assert.Empty(moved.Except(
            [nameof(Recording.ThumbnailState), nameof(Recording.ThumbnailFault)],
            StringComparer.Ordinal));
        Assert.Equal(66, before.Count);
    }

    [Theory]
    [InlineData(CaptionState.Ready, 7)]
    [InlineData(CaptionState.Absent, null)]
    [InlineData(CaptionState.Failed, null)]
    public void TakingTheCaptionsMovesTheColumnsThatAreTheCaptionsAndNoOthers(CaptionState state, int? pictures)
    {
        Recording recording = SettledWithScramblingUnresolved();
        IReadOnlyDictionary<string, string> before = Read(recording);

        recording.Caption(state, pictures, AnHourLater);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(moved);
        Assert.Empty(moved.Except(
            [
                nameof(Recording.CaptionState),
                nameof(Recording.CaptionsMadeAt),
                nameof(Recording.CaptionPictures),
                nameof(Recording.CaptionAttempts),
            ],
            StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(0)]
    [InlineData(-1)]
    public void TakingTheDataBroadcastMovesTheColumnsThatAreTheDataBroadcastAndNoOthers(int modules)
    {
        Recording recording = SettledWithScramblingUnresolved();
        IReadOnlyDictionary<string, string> before = Read(recording);

        if (modules < 0)
        {
            recording.DataBroadcastFailed(AnHourLater);
        }
        else
        {
            recording.DataBroadcastTaken(modules, AnHourLater);
        }

        recording.Descrambled(AnHourLater);

        IReadOnlyDictionary<string, string> after = Read(recording);
        string[] moved =
        [
            .. before.Where(held => !string.Equals(after[held.Key], held.Value, StringComparison.Ordinal))
                .Select(held => held.Key)
                .Order(StringComparer.Ordinal),
        ];

        Assert.NotEmpty(moved);
        Assert.Empty(moved.Except(
            [
                nameof(Recording.DataBroadcast),
                nameof(Recording.DataBroadcastState),
                nameof(Recording.DataBroadcastMadeAt),
                nameof(Recording.DataBroadcastModules),
                nameof(Recording.DataBroadcastAttempts),
                nameof(Recording.DescrambledAt),
            ],
            StringComparer.Ordinal));
    }

    private static Recording SettledWithScramblingUnresolved()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Note(RecordingFactory.Fault(RecordingFault.ScramblingUnresolved));
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, AnHourLater);

        return recording;
    }

    private static IReadOnlyDictionary<string, string> Read(Recording recording)
        => typeof(Recording)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => !Derived.Contains(property.Name))
            .ToDictionary(
                property => property.Name,
                property => Rendered(property.GetValue(recording)),
                StringComparer.Ordinal);

    private static string Rendered(object? held)
        => held switch
        {
            null => "<none>",
            string text => text,
            DateTime moment => moment.ToString("O", CultureInfo.InvariantCulture),
            IEnumerable listed => string.Join('|', listed.Cast<object?>().Select(Rendered)),
            IFormattable measured => measured.ToString(null, CultureInfo.InvariantCulture),
            _ => held.ToString() ?? "<none>",
        };

    [Fact]
    public void AnErasureThatLeftFilesBehindIsKeptWithWhenItWasAskedForAndHowManyStayed()
    {
        Recording recording = Settled();

        recording.Erased(LeftBehind(2), AnHourLater.AddMinutes(5));

        Assert.Equal(AnHourLater.AddMinutes(5), recording.LeftBehindAt);
        Assert.Equal(2, recording.FilesLeftBehind);
    }

    [Fact]
    public void AnErasureThatCouldNotCountWhatItLeftStillSaysItLeftSomething()
    {
        Recording recording = Settled();

        recording.Erased(RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied"), AnHourLater);

        Assert.Equal(AnHourLater, recording.LeftBehindAt);
        Assert.Null(recording.FilesLeftBehind);
    }

    [Fact]
    public void ALaterErasureThatLeftFilesBehindIsTheOneKept()
    {
        Recording recording = Settled();

        recording.Erased(LeftBehind(2), AnHourLater);
        recording.Erased(LeftBehind(1), AnHourLater.AddMinutes(10));

        Assert.Equal(AnHourLater.AddMinutes(10), recording.LeftBehindAt);
        Assert.Equal(1, recording.FilesLeftBehind);
    }

    [Fact]
    public void AnErasureThatTookEverythingClearsWhatAnEarlierOneLeftBehind()
    {
        Recording recording = Settled();
        recording.Erased(LeftBehind(2), AnHourLater);

        recording.Erased(RecordingErasure.Erased(2), AnHourLater.AddMinutes(10));

        Assert.Null(recording.LeftBehindAt);
        Assert.Null(recording.FilesLeftBehind);
    }

    [Theory]
    [InlineData(ErasureFault.RootOutOfReach)]
    [InlineData(ErasureFault.DriverUnreachable)]
    [InlineData(ErasureFault.DriverRefused)]
    public void AnErasureThatTouchedNothingLeavesWhatAnEarlierOneLeftBehindAsItWas(ErasureFault fault)
    {
        Recording recording = Settled();
        recording.Erased(LeftBehind(2), AnHourLater);

        recording.Erased(RecordingErasure.Refused(fault, "nothing was touched"), AnHourLater.AddMinutes(10));

        Assert.Equal(AnHourLater, recording.LeftBehindAt);
        Assert.Equal(2, recording.FilesLeftBehind);
    }

    [Theory]
    [InlineData(ErasureFault.RootOutOfReach)]
    [InlineData(ErasureFault.DriverUnreachable)]
    [InlineData(ErasureFault.DriverRefused)]
    public void AnErasureThatTouchedNothingSaysNothingWasLeftBehindOnARecordingNobodyTriedToThrowAway(
        ErasureFault fault)
    {
        Recording recording = Settled();

        recording.Erased(RecordingErasure.Refused(fault, "nothing was touched"), AnHourLater);

        Assert.Null(recording.LeftBehindAt);
        Assert.Null(recording.FilesLeftBehind);
    }

    [Fact]
    public void ARecordingStillBeingWrittenKeepsNoErasure()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<InvalidOperationException>(() => recording.Erased(LeftBehind(1), AnHourLater));
        Assert.Null(recording.LeftBehindAt);
    }

    [Fact]
    public void AnErasureIsNotKeptAtATimeBeforeTheRecordingBegan()
    {
        Recording recording = Settled();

        Assert.Throws<ArgumentException>(
            () => recording.Erased(LeftBehind(1), RecordingFactory.Now.AddMinutes(-1)));
    }

    [Fact]
    public void OnlyAnErasureThatLeftAFileBehindCountsWhatItLeft()
    {
        Assert.Throws<ArgumentException>(
            () => RecordingErasure.Refused(ErasureFault.RootOutOfReach, "the mount has gone", 1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", 0));
        Assert.True(LeftBehind(1).LeftFilesBehind);
        Assert.False(RecordingErasure.Erased(1).LeftFilesBehind);
    }

    [Fact]
    public void ARowCannotCountFilesLeftBehindWithoutSayingWhenOrOnARecordingStillBeingWritten()
    {
        Recording settled = Settled();

        Assert.Throws<ArgumentException>(() => Again(settled, leftBehindAt: null, filesLeftBehind: 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => Again(settled, AnHourLater, filesLeftBehind: 0));
        Assert.Throws<ArgumentException>(() => Again(RecordingFactory.Started(), AnHourLater, filesLeftBehind: 1));
        Assert.Equal(3, Again(settled, AnHourLater, filesLeftBehind: 3).FilesLeftBehind);
        Assert.Null(Again(settled, AnHourLater, filesLeftBehind: null).FilesLeftBehind);
    }

    private static RecordingErasure LeftBehind(int files)
        => RecordingErasure.Refused(ErasureFault.FileLeftBehind, "permission denied", files);

    private static Recording Again(Recording held, DateTime? leftBehindAt, int? filesLeftBehind)
        => Recording.Rehydrate(
            held.Id,
            held.ReservationId,
            held.Programme,
            held.OutputRoot,
            held.FileName,
            held.FileSizeObserved,
            held.ObservedAt,
            held.StartedAtActual,
            held.StoppedAtActual,
            held.AbortedAt,
            held.WrittenDurationMs,
            held.ResumeCount,
            held.Interruptions,
            held.ExpectedWindowStart,
            held.ExpectedWindowEnd,
            held.PromisedWindowEnd,
            held.Outcome,
            held.OutcomeDetail,
            held.Counters,
            held.Positions,
            held.ScrambledPackets,
            held.EovfCount,
            held.MeasuredUpdatedAt,
            held.TunerDeviceId,
            held.ThumbnailState,
            RecordingFactory.Snapshot(),
            held.BroadcastGroupKey,
            held.BroadcastGroupRole,
            held.ThumbnailFault,
            leftBehindAt,
            filesLeftBehind);

    [Fact]
    public void ARecordingBeginsWithNoPositionAtAll()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.Positions.Located);
        Assert.Empty(recording.Positions.Buckets);
    }

    [Fact]
    public void APositionCannotRideOnAMeasurementNobodyTook()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Unmeasured,
            DropTimeline.AnchoredAt(900_000),
            null,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void ATimelineCannotPlaceMoreLostPacketsThanWereCounted()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Counted(2, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            null,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
    }

    [Fact]
    public void ATimelineCannotPlaceMoreScrambledPacketsThanWereCounted()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Counted(0, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 0, 3)], []),
            1,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
    }

    [Fact]
    public void ATimelineCannotPlaceScrambledPacketsWhenNoneWereCountedAtAll()
    {
        Recording recording = RecordingFactory.Started();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => recording.Measure(
            DropCounters.Counted(0, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 0, 1)], []),
            null,
            0,
            Now));

        Assert.Equal("positions", refusal.ParamName);
    }

    [Fact]
    public void AMeasurementWithPositionsKeepsBoth()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(
            DropCounters.Counted(3, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            null,
            0,
            Now);

        Assert.Equal(3, recording.Counters.Dropped);
        Assert.True(recording.Positions.Located);
        Assert.Equal(12, Assert.Single(recording.Positions.Buckets).Second);
    }

    [Fact]
    public void AMeasurementTakenWithoutPositionsIsStillAMeasurement()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(3, 1000), DropTimeline.Unlocated, null, 0, Now);

        Assert.True(recording.Counters.Measured);
        Assert.False(recording.Positions.Located);
    }

    [Fact]
    public void MeasurementThatBreaksMidWayTakesThePositionsWithIt()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(
            DropCounters.Counted(3, 1000),
            DropTimeline.Rehydrate(900_000, [new DropBucket(12, 3, 0)], []),
            null,
            0,
            Now);

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Now.AddMinutes(1));

        Assert.False(recording.Positions.Located);
        Assert.Empty(recording.Positions.Buckets);
    }

    [Fact]
    public void ARehydratedPositionOnAnUncountedRecordingIsRefused()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now.AddMinutes(-5),
            null,
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.AnchoredAt(900_000),
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("positions", refusal.ParamName);
    }

    [Fact(DisplayName = "BR-KD-030: a recording nothing has counted yet is counted as far as the moment it began")]
    public void ARecordingNothingCountedIsCountedAsFarAsItsStart()
        => Assert.Equal(Now, RecordingFactory.Started().CountedUntil);

    [Fact(DisplayName = "BR-KD-030: a recording is counted as far as the last count it took")]
    public void ARecordingIsCountedAsFarAsItsLastCount()
    {
        Recording recording = RecordingFactory.Started();

        recording.Measure(DropCounters.Counted(0, 10), DropTimeline.Unlocated, null, 0, Now.AddMinutes(3));

        Assert.Equal(Now.AddMinutes(3), recording.CountedUntil);
    }

    [Fact(DisplayName = "BR-KD-005: a running recording takes a count from a moment later than the one it was last counted at, and not one at or before it")]
    public void ARunningRecordingTakesOnlyALaterCount()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(0, 10), DropTimeline.Unlocated, null, 0, Now.AddMinutes(3));

        Assert.False(recording.KeepsCountingAt(Now.AddMinutes(3)));
        Assert.True(recording.KeepsCountingAt(Now.AddMinutes(3).AddTicks(1)));
    }

    [Fact(DisplayName = "BR-KD-005: a recording that is over takes no count, however late it comes")]
    public void ARecordingThatIsOverTakesNoCount()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.KeepsCountingAt(recording.ExpectedWindowEnd));

        recording.Abort(Now.AddMinutes(1));

        Assert.False(recording.KeepsCountingAt(Now.AddMinutes(2)));
    }

    [Fact(DisplayName = "BR-KD-026: a gap is kept when it falls after the start and after every gap already kept")]
    public void AGapIsKeptWhenItFollowsEveryGapAlreadyKept()
    {
        Recording recording = RecordingFactory.Started();
        RecordingGap first = new(Now.AddMinutes(5), Now.AddMinutes(6));

        Assert.True(recording.Follows(first));

        recording.Missed(first);

        Assert.False(recording.Follows(first));
        Assert.False(recording.Follows(new RecordingGap(Now.AddMinutes(4), Now.AddMinutes(6))));
        Assert.False(recording.Follows(new RecordingGap(Now.AddMinutes(5).AddSeconds(30), Now.AddMinutes(7))));
        Assert.True(recording.Follows(new RecordingGap(Now.AddMinutes(6), Now.AddMinutes(7))));
    }

    [Fact(DisplayName = "BR-KD-026: a gap that begins before the recording began is not one of its own")]
    public void AGapBeforeTheStartIsNotKept()
        => Assert.False(RecordingFactory.Started().Follows(new RecordingGap(Now.AddTicks(-1), Now.AddMinutes(1))));

    [Fact(DisplayName = "BR-KD-003: a recording is interrupted while its last break is open, and not once it has resumed")]
    public void ARecordingIsInterruptedWhileItsLastBreakIsOpen()
    {
        Recording recording = RecordingFactory.Started();

        Assert.False(recording.IsInterrupted);

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(1));

        Assert.True(recording.IsInterrupted);

        recording.Resume(Now.AddMinutes(2));

        Assert.False(recording.IsInterrupted);
    }

    [Fact(DisplayName = "BR-KD-028: a recording stopped being written at the last write to its file")]
    public void ARecordingStoppedBeingWrittenAtTheLastWrite()
        => Assert.Equal(
            Now.AddMinutes(4),
            RecordingFactory.Started().StoppedWritingAt(Now.AddMinutes(4), Now.AddMinutes(5)));

    [Fact(DisplayName = "BR-KD-028: a file that says nothing, or says it was written at or after the break was noticed, leaves the moment it was noticed")]
    public void AFileThatSaysNothingUsefulLeavesTheMomentItWasNoticed()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(Now.AddMinutes(5), recording.StoppedWritingAt(null, Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), recording.StoppedWritingAt(Now.AddMinutes(5), Now.AddMinutes(5)));
        Assert.Equal(Now.AddMinutes(5), recording.StoppedWritingAt(Now.AddMinutes(6), Now.AddMinutes(5)));
    }

    [Fact(DisplayName = "BR-KD-028: a recording never stopped being written before it began, or before it last resumed")]
    public void ARecordingNeverStoppedBeforeItsLastMark()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(Now, recording.StoppedWritingAt(Now.AddMinutes(-1), Now.AddMinutes(5)));

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(1));
        recording.Resume(Now.AddMinutes(2));

        Assert.Equal(Now.AddMinutes(2), recording.StoppedWritingAt(Now.AddMinutes(1).AddSeconds(30), Now.AddMinutes(5)));

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(3));

        Assert.Equal(Now.AddMinutes(3), recording.StoppedWritingAt(Now.AddMinutes(2).AddSeconds(30), Now.AddMinutes(5)));
    }

    [Fact(DisplayName = "BR-KD-004: a recording names the tuner it was first written on and keeps that one")]
    public void ARecordingKeepsTheFirstTunerItNames()
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Adopt(string.Empty);

        Assert.Null(recording.TunerDeviceId);

        recording.Adopt("pt3-2");
        recording.Adopt("pt3-3");

        Assert.Equal(new TunerDeviceId("pt3-2"), recording.TunerDeviceId);
    }

    [Fact(DisplayName = "BR-KD-009: a programme whose end is still undecided is written down once, however often it is followed")]
    public void AnUndecidedEndIsWrittenDownOnce()
    {
        Recording recording = RecordingFactory.Started();

        recording.NoteEndUndecided(Now.AddMinutes(1));
        recording.NoteEndUndecided(Now.AddMinutes(2));

        OutcomeDetail noted = Assert.Single(recording.OutcomeDetail);
        Assert.Equal(RecordingFault.EndStillUndecided, noted.Fault);
        Assert.Equal(Now.AddMinutes(1), noted.NoticedAt);
    }

    [Fact(DisplayName = "BR-KD-010: a recording starts on a disk found short of room and says so")]
    public void ARecordingOnADiskFoundShortSaysSo()
    {
        Recording roomy = RecordingFactory.Started();
        Recording tight = RecordingFactory.Started();

        roomy.Prechecked(DiskPrecheckVerdict.Of(null, 1_000, 10_000, 1), Now);
        tight.Prechecked(DiskPrecheckVerdict.Of(DiskShortfall.ShortOfTheEstimate, 10_000, 1_000, 1), Now);

        Assert.Empty(roomy.OutcomeDetail);
        Assert.Equal(
            [new OutcomeDetail(RecordingFault.RefusedByDiskPrecheck, null, string.Empty, Now)],
            tight.OutcomeDetail);
    }

    [Fact]
    public void ARecordingIsPromisedTheEndItBeganWith()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(Ends, recording.PromisedWindowEnd);
        Assert.Equal(recording.ExpectedWindowEnd, recording.PromisedWindowEnd);
    }

    [Fact]
    public void FollowingAProgrammeLaterMovesTheEndAndNeverThePromise()
    {
        Recording recording = RecordingFactory.Started();

        recording.Extend(Ends.AddMinutes(15));
        recording.Extend(Ends.AddMinutes(40));

        Assert.Equal(Ends.AddMinutes(40), recording.ExpectedWindowEnd);
        Assert.Equal(Ends, recording.PromisedWindowEnd);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-20)]
    public void AnEndThatIsNotLaterIsRefusedAndMovesNeitherTheEndNorThePromise(int minutes)
    {
        Recording recording = RecordingFactory.Started();
        recording.Extend(Ends.AddMinutes(10));

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => recording.Extend(Ends.AddMinutes(10 + minutes)));

        Assert.Equal("expectedWindowEnd", refusal.ParamName);
        Assert.Equal(Ends.AddMinutes(10), recording.ExpectedWindowEnd);
        Assert.Equal(Ends, recording.PromisedWindowEnd);
    }

    [Fact]
    public void ARehydratedRecordingKeepsThePromiseItWasReadWith()
    {
        Recording recording = Rehydrated(Ends.AddMinutes(30), Ends);

        Assert.Equal(Ends.AddMinutes(30), recording.ExpectedWindowEnd);
        Assert.Equal(Ends, recording.PromisedWindowEnd);
    }

    [Fact]
    public void APromiseLaterThanTheEndARecordingHasIsRefused()
        => Assert.Equal(
            "promisedWindowEnd",
            Assert.Throws<ArgumentException>(() => Rehydrated(Ends, Ends.AddSeconds(1))).ParamName);

    [Fact]
    public void APromiseThatDoesNotComeAfterTheWindowStartsIsRefused()
        => Assert.Equal(
            "promisedWindowEnd",
            Assert.Throws<ArgumentException>(() => Rehydrated(Ends, Starts)).ParamName);

    private static Recording Rehydrated(DateTime expectedEnd, DateTime promisedEnd)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now,
            null,
            null,
            0,
            0,
            [],
            Starts,
            expectedEnd,
            promisedEnd,
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);
    }

    [Theory(DisplayName = "BR-KD-010: a recording whose disk filled fails, naming the full disk and what its file weighed, and keeps the size it was weighed at")]
    [MemberData(nameof(EverySize))]
    public void ARecordingWhoseDiskFilledFails(long? weighed)
    {
        Recording recording = RecordingFactory.Started();

        recording.FailOnAFullDisk(weighed, HalfAnHourLater);

        Assert.Equal(RecordingOutcome.Failed, recording.Outcome);
        Assert.Equal(RecordingFaults.OfAFullDisk(weighed), recording.OutcomeDetail.Select(detail => detail.Fault));
        Assert.All(recording.OutcomeDetail, detail => Assert.Equal(HalfAnHourLater, detail.NoticedAt));
        Assert.Equal(weighed ?? 0, recording.FileSizeObserved);
        Assert.Equal(HalfAnHourLater, recording.StoppedAtActual);
    }

    [Theory(DisplayName = "BR-KS-002: a recording nothing was writing ends on what its file holds, naming why it ended where it did")]
    [InlineData(false, 4_096_000L)]
    [InlineData(true, 4_096_000L)]
    [InlineData(false, 0L)]
    [InlineData(true, null)]
    public void ARecordingNothingWasWritingEndsOnWhatItsFileHolds(bool another, long? weighed)
    {
        Recording recording = RecordingFactory.Started();

        recording.SettleOnWhatWasLeft(another, weighed, AsShipped, HalfAnHourLater);

        Assert.Equal(OrphanRecovery.WhatIsLeftOf(weighed), recording.Outcome);
        Assert.Equal(
            OrphanRecovery.WhyItEndedWhereItDid(another, weighed, QualityLevel.Unmeasured),
            recording.OutcomeDetail.Select(detail => detail.Fault));
        Assert.Equal(weighed ?? 0, recording.FileSizeObserved);
    }

    [Fact(DisplayName = "BR-KS-002: a recording nothing was writing that was left scrambled says so as it ends")]
    public void ARecordingLeftScrambledSaysSoAsItEnds()
    {
        Recording recording = RecordingFactory.Started();
        recording.Measure(DropCounters.Counted(0, 1_000_000), DropTimeline.Unlocated, 900_000, 0, Now.AddMinutes(1));

        recording.SettleOnWhatWasLeft(false, 4_096_000, AsShipped, HalfAnHourLater);

        Assert.Contains(RecordingFault.ScramblingUnresolved, recording.OutcomeDetail.Select(detail => detail.Fault));
        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
    }

    [Theory(DisplayName = "BR-KD-002: a recording ends with what the completion judgement says of its own window, length, stop and file")]
    [InlineData(true, 1_200_000_000L, 55)]
    [InlineData(true, 1_000L, 55)]
    [InlineData(false, 1_200_000_000L, 55)]
    [InlineData(true, null, 55)]
    [InlineData(true, 0L, 5)]
    [InlineData(true, 600_000_000L, 20)]
    public void ARecordingEndsWithWhatTheJudgementSays(bool askedToStop, long? weighed, int minutesWritten)
    {
        Recording recording = RecordingFactory.Started();
        recording.Wrote(TimeSpan.FromMinutes(minutesWritten));

        if (askedToStop)
        {
            recording.Abort(recording.ExpectedWindowEnd);
        }

        RecordingVerdict expected = CompletionEvaluator.Judge(
            new RecordingEvidence(
                weighed,
                recording.Written,
                recording.ExpectedWindowStart,
                recording.ExpectedWindowEnd,
                recording.AbortedAt,
                QualityLevel.Unmeasured),
            ExpectedBitrate.Terrestrial,
            CompletionTolerance.Default);

        recording.Conclude(weighed, ExpectedBitrate.Terrestrial, AsShipped, HalfAnHourLater);

        Assert.Equal(expected.Outcome, recording.Outcome);
        Assert.Equal(expected.Detail(HalfAnHourLater), recording.OutcomeDetail);
        Assert.Equal(weighed ?? 0, recording.FileSizeObserved);
    }

    [Fact(DisplayName = "BR-KD-002: a recording judged complete on its own evidence is complete only because this side asked it to stop")]
    public void OnlyARecordingAskedToStopIsJudgedComplete()
    {
        Recording asked = RecordingFactory.Started();
        Recording unasked = RecordingFactory.Started();

        foreach (Recording recording in new[] { asked, unasked })
        {
            recording.Wrote(recording.ExpectedWindowEnd - recording.ExpectedWindowStart);
        }

        asked.Abort(asked.ExpectedWindowEnd);

        asked.Conclude(6_800_000_000, ExpectedBitrate.Terrestrial, AsShipped, HalfAnHourLater);
        unasked.Conclude(6_800_000_000, ExpectedBitrate.Terrestrial, AsShipped, HalfAnHourLater);

        Assert.Equal(RecordingOutcome.Complete, asked.Outcome);
        Assert.NotEqual(RecordingOutcome.Complete, unasked.Outcome);
    }

    [Fact(DisplayName = "BR-KS-001: a recording the driver ended at the end it was holding is read as stopped there by this side")]
    public void ARecordingEndedAtItsHeldEndIsReadAsStoppedThere()
    {
        Recording recording = RecordingFactory.Started();
        DateTime held = Now.AddMinutes(20);

        recording.ReachedItsEnd(held, HalfAnHourLater);

        Assert.Equal(held, recording.AbortedAt);
    }

    [Fact(DisplayName = "BR-KS-001: an end held later than now, or none at all, is read as reached now")]
    public void AnEndHeldLaterThanNowOrNoneIsReadAsReachedNow()
    {
        Recording later = RecordingFactory.Started();
        Recording unnamed = RecordingFactory.Started();

        later.ReachedItsEnd(HalfAnHourLater.AddMinutes(5), HalfAnHourLater);
        unnamed.ReachedItsEnd(null, HalfAnHourLater);

        Assert.Equal(HalfAnHourLater, later.AbortedAt);
        Assert.Equal(HalfAnHourLater, unnamed.AbortedAt);
    }

    [Fact(DisplayName = "BR-KS-001: an end held before the recording began is no end this recording reached, so nobody asked for it to stop")]
    public void AnEndBeforeTheRecordingBeganIsNotOneItReached()
    {
        Recording recording = RecordingFactory.Started();

        recording.ReachedItsEnd(recording.StartedAtActual.AddTicks(-1), HalfAnHourLater);

        Assert.Null(recording.AbortedAt);

        recording.ReachedItsEnd(recording.StartedAtActual, HalfAnHourLater);

        Assert.Equal(recording.StartedAtActual, recording.AbortedAt);
    }

    [Fact(DisplayName = "BR-KD-006: a stop this side already wrote down stands when the driver later says it reached its end")]
    public void AStopAlreadyWrittenDownStands()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now.AddMinutes(10));

        recording.ReachedItsEnd(Now.AddMinutes(20), HalfAnHourLater);

        Assert.Equal(Now.AddMinutes(10), recording.AbortedAt);
    }

    [Fact]
    public void ARecordingStartsWithNoPictureYet()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(ThumbnailState.Pending, recording.ThumbnailState);
        Assert.Null(recording.ThumbnailFault);
    }

    [Theory]
    [InlineData(ThumbnailState.Ready)]
    [InlineData(ThumbnailState.Skipped)]
    [InlineData(ThumbnailState.Pending)]
    public void ARecordingHoldsTheStatesThatNameNoFault(ThumbnailState state)
    {
        Recording recording = RecordingFactory.Started();

        recording.Illustrate(state);

        Assert.Equal(state, recording.ThumbnailState);
        Assert.Null(recording.ThumbnailFault);
    }

    [Theory]
    [InlineData(ThumbnailFault.ProgrammeMissing)]
    [InlineData(ThumbnailFault.SourceOutOfReach)]
    [InlineData(ThumbnailFault.Refused)]
    [InlineData(ThumbnailFault.TimedOut)]
    [InlineData(ThumbnailFault.NothingWasWritten)]
    public void APictureThatCouldNotBeDrawnSaysWhatStoppedIt(ThumbnailFault fault)
    {
        Recording recording = RecordingFactory.Started();

        recording.Illustrate(ThumbnailState.Failed, fault);

        Assert.Equal(ThumbnailState.Failed, recording.ThumbnailState);
        Assert.Equal(fault, recording.ThumbnailFault);
    }

    [Fact]
    public void AThumbnailStateTheLedgerDoesNotHoldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingFactory.Started().Illustrate((ThumbnailState)99));

    [Fact]
    public void AThumbnailFaultTheLedgerDoesNotHoldIsRefused()
        => Assert.Equal(
            "thumbnailFault",
            Assert.Throws<ArgumentOutOfRangeException>(
                () => RecordingFactory.Started().Illustrate(ThumbnailState.Failed, (ThumbnailFault)99)).ParamName);

    [Fact]
    public void AFailureThatNamesNothingIsRefused()
        => Assert.Equal(
            "thumbnailFault",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Illustrate(ThumbnailState.Failed)).ParamName);

    [Theory]
    [InlineData(ThumbnailState.Ready)]
    [InlineData(ThumbnailState.Skipped)]
    [InlineData(ThumbnailState.Pending)]
    public void AFaultOnAPictureNothingStoppedIsRefused(ThumbnailState state)
        => Assert.Equal(
            "thumbnailFault",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Illustrate(state, ThumbnailFault.TimedOut)).ParamName);

    [Fact]
    public void AskingAgainClearsTheFaultTheFailureLeftBehind()
    {
        Recording recording = RecordingFactory.Started();
        recording.Illustrate(ThumbnailState.Failed, ThumbnailFault.ProgrammeMissing);

        recording.Illustrate(ThumbnailState.Pending);

        Assert.Equal(ThumbnailState.Pending, recording.ThumbnailState);
        Assert.Null(recording.ThumbnailFault);
    }

    [Fact]
    public void ARecordingThatFailedGetsNoPicture()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Failed, 0, Now.AddHours(1));

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => recording.Illustrate(ThumbnailState.Ready));

        Assert.Equal("thumbnailState", refusal.ParamName);

        recording.Illustrate(ThumbnailState.Skipped);
        Assert.Equal(ThumbnailState.Skipped, recording.ThumbnailState);
    }

    [Fact]
    public void ARecordingThatFailsAfterItsPictureWasMadeIsRefused()
    {
        Recording recording = RecordingFactory.Started();
        recording.Illustrate(ThumbnailState.Ready);
        recording.Note(RecordingFactory.Fault());

        Assert.Equal(
            "thumbnailState",
            Assert.Throws<ArgumentException>(
                () => recording.Settle(RecordingOutcome.Failed, 0, Now.AddHours(1))).ParamName);
    }

    [Fact]
    public void ATruncatedRecordingMayStillHaveAPicture()
    {
        Recording recording = Settled(RecordingOutcome.Truncated);

        recording.Illustrate(ThumbnailState.Ready);

        Assert.Equal(ThumbnailState.Ready, recording.ThumbnailState);
    }

    [Fact]
    public void APictureOfATruncatedRecordingSaysTheRecordingIsUnfinished()
    {
        Recording recording = Settled(RecordingOutcome.Truncated);
        Assert.False(recording.ThumbnailShowsAnUnfinishedRecording);

        recording.Illustrate(ThumbnailState.Ready);

        Assert.True(recording.ThumbnailShowsAnUnfinishedRecording);
    }

    [Fact]
    public void APictureOfARecordingThatFinishedSaysNoSuchThing()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Now.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(1));

        recording.Illustrate(ThumbnailState.Ready);

        Assert.False(recording.ThumbnailShowsAnUnfinishedRecording);
    }

    [Theory]
    [InlineData(ThumbnailState.Pending)]
    [InlineData(ThumbnailState.Skipped)]
    public void ATruncatedRecordingWithNoPictureShowsNothingAtAll(ThumbnailState state)
    {
        Recording recording = Settled(RecordingOutcome.Truncated);

        recording.Illustrate(state);

        Assert.False(recording.ThumbnailShowsAnUnfinishedRecording);
    }

    [Theory]
    [InlineData(ThumbnailState.Ready)]
    [InlineData(ThumbnailState.Skipped)]
    [InlineData(ThumbnailState.Pending)]
    public void DrawingAPictureChangesNothingAboutHowTheRecordingEnded(ThumbnailState state)
    {
        Recording recording = Settled(RecordingOutcome.Truncated);

        recording.Illustrate(state);

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        Assert.Equal(1_200_000, recording.FileSizeObserved);
        Assert.Equal(Now.AddHours(1), recording.StoppedAtActual);
        Assert.Equal([RecordingFault.DriverLost], recording.OutcomeDetail.Select(detail => detail.Fault));
    }

    [Fact]
    public void FailingToDrawOneChangesNothingAboutHowTheRecordingEndedEither()
    {
        Recording recording = Settled(RecordingOutcome.Truncated);

        recording.Illustrate(ThumbnailState.Failed, ThumbnailFault.ProgrammeMissing);

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        Assert.Equal(1_200_000, recording.FileSizeObserved);
        Assert.Equal(Now.AddHours(1), recording.StoppedAtActual);
        Assert.Equal([RecordingFault.DriverLost], recording.OutcomeDetail.Select(detail => detail.Fault));
    }

    [Fact]
    public void ARowThatCameBackFailedAndNamedNothingIsRefused()
        => Assert.Equal(
            "thumbnailFault",
            Assert.Throws<ArgumentException>(() => Rehydrated(ThumbnailState.Failed, null)).ParamName);

    [Fact]
    public void ARowThatCameBackNamingAFaultItDidNotSufferIsRefused()
        => Assert.Equal(
            "thumbnailFault",
            Assert.Throws<ArgumentException>(
                () => Rehydrated(ThumbnailState.Ready, ThumbnailFault.TimedOut)).ParamName);

    [Fact]
    public void ARowThatCameBackWithAFaultAndTheStateToMatchIsRead()
    {
        Recording recording = Rehydrated(ThumbnailState.Failed, ThumbnailFault.Refused);

        Assert.Equal(ThumbnailState.Failed, recording.ThumbnailState);
        Assert.Equal(ThumbnailFault.Refused, recording.ThumbnailFault);
    }

    private static Recording Rehydrated(ThumbnailState state, ThumbnailFault? fault)
    {
        RecordingId id = RecordingId.New();

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now.AddMinutes(-5),
            null,
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            state,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            fault);
    }

    private static Recording Settled(RecordingOutcome outcome = RecordingOutcome.Truncated)
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(outcome, 1_200_000, Now.AddHours(1));

        return recording;
    }

    [Fact]
    public void ARecordingDoesNotStopBeforeItStarted()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => recording.Settle(RecordingOutcome.Failed, 0, Began.AddSeconds(-1))).ParamName);
    }

    [Fact]
    public void ARecordingIsNotAbortedBeforeItStarted()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Abort(Began.AddSeconds(-1))).ParamName);

    [Fact]
    public void ARecordingIsNotInterruptedBeforeItStarted()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Interrupt(RecordingFault.DriverLost, Began.AddSeconds(-1)))
                .ParamName);

    [Fact]
    public void ARecordingIsNotResumedBeforeItWasInterrupted()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now);

        Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => recording.Resume(Now.AddSeconds(-1))).ParamName);
    }

    [Fact]
    public void ARecordingIsNotMeasuredBeforeItStarted()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => RecordingFactory.Started().Measure(
                DropCounters.Counted(0, 10),
                DropTimeline.Unlocated,
                null,
                0,
                Began.AddSeconds(-1))).ParamName);

    [Theory]
    [InlineData("stoppedAtActual")]
    [InlineData("abortedAt")]
    [InlineData("observedAt")]
    [InlineData("measuredUpdatedAt")]
    public void ARehydratedRecordingHasNothingHappeningBeforeItStarted(string column)
    {
        DateTime before = Began.AddSeconds(-1);
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            column is "observedAt" ? 12 : null,
            column is "observedAt" ? before : null,
            Began,
            column is "stoppedAtActual" ? before : null,
            column is "abortedAt" ? before : null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            column is "measuredUpdatedAt" ? before : null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal(column, refusal.ParamName);
    }

    [Fact]
    public void AReasonIsNotNoticedBeforeTheRecordingStarted()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "outcomeDetail",
            Assert.Throws<ArgumentException>(() => recording.Note(
                new OutcomeDetail(RecordingFault.DiskExhausted, null, string.Empty, Began.AddSeconds(-1))))
                .ParamName);
    }

    [Fact]
    public void ASecondInterruptionDoesNotReachBackBeforeTheFirstOneWasResumed()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(10));
        recording.Resume(Now.AddMinutes(11));

        Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(10).AddSeconds(30))).ParamName);

        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(12));
        Assert.Equal(2, recording.Interruptions.Count);
    }

    [Fact]
    public void ASecondInterruptionDoesNotReachBackBeforeTheFirstOneEvenWhileItIsOpen()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(10));
        recording.Resume(Now.AddMinutes(10));

        Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => recording.Interrupt(RecordingFault.DriverLost, Now.AddMinutes(9))).ParamName);
    }

    [Fact]
    public void ARehydratedReasonIsNotNoticedBeforeTheRecordingStarted()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Began,
            null,
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [new OutcomeDetail(RecordingFault.DiskExhausted, null, string.Empty, Began.AddSeconds(-1))],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("outcomeDetail", refusal.ParamName);
    }

    [Fact]
    public void ARecordingThatStoppedTheMomentItStartedIsStillAllowed()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(Began);
        recording.Note(RecordingFactory.Fault());

        recording.Settle(RecordingOutcome.Failed, 0, Began);

        Assert.Equal(Began, recording.StoppedAtActual);
    }

    [Theory]
    [InlineData("pt3-0")]
    [InlineData("px4_0.1")]
    public void ATunerDeviceIdIsANameTheDriverDetected(string name)
        => Assert.Equal(name, new TunerDeviceId(name).Value);

    [Theory]
    [InlineData("/dev/dvb/adapter0")]
    [InlineData("pt3 0")]
    [InlineData("")]
    public void ATunerDeviceIdIsNeverAPath(string name)
        => Assert.ThrowsAny<ArgumentException>(() => new TunerDeviceId(name));

    [Fact]
    public void ATunerDeviceIdIsShortEnoughToCrossTheWire()
        => Assert.Throws<ArgumentException>(
            () => new TunerDeviceId(new string('a', TunerDeviceId.MaxLength + 1)));

    [Fact]
    public void ARecordingNamesTheTunerItWasWrittenBy()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("pt3-1"));

        Assert.Equal(new TunerDeviceId("pt3-1"), recording.TunerDeviceId);
    }

    [Fact]
    public void ARecordingThatHasNotAcquiredATunerYetNamesNone()
    {
        Recording recording = RecordingFactory.Unclaimed();

        Assert.Null(recording.TunerDeviceId);
    }

    [Fact]
    public void ARecordingTakesTheTunerItWasGivenWhenTheSessionOpens()
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Acquire(new TunerDeviceId("pt3-2"));

        Assert.Equal(new TunerDeviceId("pt3-2"), recording.TunerDeviceId);
    }

    [Fact]
    public void ACountThatCameOffNoTunerIsRefused()
    {
        Recording recording = RecordingFactory.Unclaimed();

        ArgumentException counted = Assert.Throws<ArgumentException>(
            () => recording.Measure(DropCounters.Counted(0, 1000), DropTimeline.Unlocated, null, 0, Now));
        Assert.Equal("tunerDeviceId", counted.ParamName);

        ArgumentException overflowed = Assert.Throws<ArgumentException>(
            () => recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 3, Now));
        Assert.Equal("tunerDeviceId", overflowed.ParamName);
    }

    [Fact]
    public void ARecordingWithNoTunerMayStillSayItCountedNothing()
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Measure(DropCounters.Unmeasured, DropTimeline.Unlocated, null, 0, Now);

        Assert.False(recording.Counters.Measured);
        Assert.Equal(0, recording.EovfCount);
    }

    [Fact]
    public void ARehydratedCountThatCameOffNoTunerIsRefused()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            null,
            null,
            Now.AddMinutes(-5),
            null,
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            null,
            [],
            DropCounters.Counted(0, 1000),
            DropTimeline.Unlocated,
            null,
            0,
            Now,
            null,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("tunerDeviceId", refusal.ParamName);
    }

    [Theory]
    [InlineData(RecordingFault.TuneFailed)]
    [InlineData(RecordingFault.DriverLost)]
    [InlineData(RecordingFault.TunerContended)]
    [InlineData(RecordingFault.ScramblingUnresolved)]
    public void AReasonThatReachedATunerNamesWhichTunerItReached(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Unclaimed();

        ArgumentException refusal = Assert.Throws<ArgumentException>(
            () => recording.Note(new OutcomeDetail(fault, null, string.Empty, Now)));

        Assert.Equal("tunerDeviceId", refusal.ParamName);
        Assert.Empty(recording.OutcomeDetail);
    }

    [Theory]
    [InlineData(RecordingFault.RefusedByDiskPrecheck)]
    [InlineData(RecordingFault.DiskExhausted)]
    [InlineData(RecordingFault.StoppedByHand)]
    [InlineData(RecordingFault.DrainGraceExpired)]
    [InlineData(RecordingFault.ShortOfTheWindow)]
    public void AReasonThatNeverReachedATunerNeedNotNameOne(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Unclaimed();

        recording.Note(new OutcomeDetail(fault, null, string.Empty, Now));

        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public void AReasonThatReachedATunerIsTakenOnceTheTunerIsKnown()
    {
        Recording recording = RecordingFactory.Unclaimed();
        recording.Acquire(new TunerDeviceId("pt3-2"));

        recording.Note(new OutcomeDetail(RecordingFault.TuneFailed, TuneFailureKind.NoLock, string.Empty, Now));

        Assert.Single(recording.OutcomeDetail);
    }

    [Fact]
    public void ARehydratedReasonThatReachedATunerNamesWhichOne()
    {
        RecordingId id = RecordingId.New();

        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            0,
            Now.AddHours(1),
            Now,
            Now.AddHours(1),
            null,
            0,
            0,
            [],
            Now.AddMinutes(-5),
            Now.AddMinutes(55),
            Now.AddMinutes(55),
            RecordingOutcome.Failed,
            [new OutcomeDetail(RecordingFault.TunerContended, null, string.Empty, Now)],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            null,
            null,
            ThumbnailState.Skipped,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone));

        Assert.Equal("tunerDeviceId", refusal.ParamName);
    }

    [Fact]
    public void ASettledRecordingKeepsTheTunerItWasWrittenBy()
    {
        Recording recording = RecordingFactory.Started(tuner: new TunerDeviceId("pt3-3"));
        recording.Abort(Now.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 3_400_000_000, Now.AddHours(1));

        Assert.Equal(new TunerDeviceId("pt3-3"), recording.TunerDeviceId);
        Assert.Throws<InvalidOperationException>(() => recording.Acquire(new TunerDeviceId("pt3-4")));
    }

    [Theory]
    [InlineData("startedAtActual")]
    [InlineData("stoppedAtActual")]
    [InlineData("abortedAt")]
    [InlineData("observedAt")]
    [InlineData("measuredUpdatedAt")]
    [InlineData("expectedWindowStart")]
    [InlineData("expectedWindowEnd")]
    [InlineData("promisedWindowEnd")]
    public void EveryTimeARehydratedRecordingCarriesIsInUtc(string parameter)
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Rehydrated(parameter, Local));

        Assert.Equal(parameter, refusal.ParamName);
        Assert.Contains("UTC", refusal.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("startedAtActual")]
    [InlineData("expectedWindowEnd")]
    public void ATimeWithNoKindAtAllIsRefusedJustTheSame(string parameter)
        => Assert.Equal(parameter, Assert.Throws<ArgumentException>(() => Rehydrated(parameter, Unspecified)).ParamName);

    [Fact]
    public void TheTimeAnExtensionMovesToIsInUtc()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Equal(
            "expectedWindowEnd",
            Assert.Throws<ArgumentException>(
                () => recording.Extend(DateTime.SpecifyKind(Now.AddHours(2), DateTimeKind.Local))).ParamName);
    }

    [Fact]
    public void TheTimeAMeasurementWasTakenIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => RecordingFactory.Started().Measure(
                DropCounters.Unmeasured,
                DropTimeline.Unlocated,
                null,
                0,
                Local)).ParamName);

    [Fact]
    public void TheTimeAnInterruptionHappenedIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Interrupt(RecordingFault.DriverLost, Local)).ParamName);

    [Fact]
    public void TheTimeARecordingResumedIsInUtc()
    {
        Recording recording = RecordingFactory.Started();
        recording.Interrupt(RecordingFault.DriverLost, Now);

        Assert.Equal("at", Assert.Throws<ArgumentException>(() => recording.Resume(Local)).ParamName);
    }

    [Fact]
    public void TheTimeARecordingWasAbortedIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(() => RecordingFactory.Started().Abort(Local)).ParamName);

    [Fact]
    public void TheTimeARecordingSettledIsInUtc()
        => Assert.Equal(
            "at",
            Assert.Throws<ArgumentException>(
                () => RecordingFactory.Started().Settle(RecordingOutcome.Complete, 12, Local)).ParamName);

    [Fact]
    public void TheTimeInsideAnInterruptionIsInUtcOnBothEnds()
    {
        Assert.Equal(
            "OccurredAt",
            Assert.Throws<ArgumentException>(
                () => new Interruption(RecordingFault.DriverLost, Local, null)).ParamName);
        Assert.Equal(
            "ResumedAt",
            Assert.Throws<ArgumentException>(
                () => new Interruption(RecordingFault.DriverLost, Now, Local)).ParamName);
    }

    [Fact]
    public void TheTimeInsideAnOutcomeDetailIsInUtc()
        => Assert.Equal(
            "NoticedAt",
            Assert.Throws<ArgumentException>(
                () => new OutcomeDetail(RecordingFault.DriverLost, null, string.Empty, Local)).ParamName);

    [Fact]
    public void ARecordingResumesAfterItWasInterrupted()
        => Assert.Equal(
            "resumedAt",
            Assert.Throws<ArgumentException>(
                () => new Interruption(RecordingFault.DriverLost, Now, Now.AddSeconds(-1))).ParamName);

    [Fact]
    public void AFaultInsideTheHistoryIsOneTheLedgerHolds()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new Interruption((RecordingFault)99, Now, null));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new OutcomeDetail((RecordingFault)99, null, string.Empty, Now));
    }

    [Fact]
    public void ATuneFailureInsideTheHistoryIsOneOfTheFourKinds()
        => Assert.Throws<ArgumentOutOfRangeException>(() => new OutcomeDetail(
            RecordingFault.TuneFailed,
            (Carina.Domain.Channels.TuneFailureKind)99,
            string.Empty,
            Now));

    [Fact]
    public void InterruptionsAreKeptInTheOrderTheyHappened()
        => Assert.Equal(
            "interruptions",
            Assert.Throws<ArgumentException>(() => RehydratedWith(
                [
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), Now.AddMinutes(5)),
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), Now.AddMinutes(2)),
                ],
                2)).ParamName);

    [Fact]
    public void OnlyTheLastInterruptionIsStillOpen()
        => Assert.Equal(
            "interruptions",
            Assert.Throws<ArgumentException>(() => RehydratedWith(
                [
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), null),
                    new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), Now.AddMinutes(5)),
                ],
                1)).ParamName);

    [Fact]
    public void TheResumeCountIsTheNumberOfInterruptionsThatWereClosed()
    {
        Assert.Equal(
            "resumeCount",
            Assert.Throws<ArgumentException>(() => RehydratedWith(
                [new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), Now.AddMinutes(2))],
                2)).ParamName);

        Recording counted = RehydratedWith(
            [
                new Interruption(RecordingFault.DriverLost, Now.AddMinutes(1), Now.AddMinutes(2)),
                new Interruption(RecordingFault.DriverLost, Now.AddMinutes(4), null),
            ],
            1);

        Assert.Equal(1, counted.ResumeCount);
    }

    private static Recording RehydratedWith(IReadOnlyList<Interruption> interruptions, int resumeCount)
        => Build(interruptions, resumeCount, null, null);

    private static Recording Rehydrated(string parameter, DateTime local)
        => Build([], 0, parameter, local);

    private static Recording Build(
        IReadOnlyList<Interruption> interruptions,
        int resumeCount,
        string? parameter,
        DateTime? local)
    {
        RecordingId id = RecordingId.New();
        DateTime At(string name) => parameter == name ? local!.Value : Now;

        return Recording.Rehydrate(
            id,
            null,
            RecordingFactory.Programme(),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            parameter is "observedAt" ? 12 : null,
            parameter is "observedAt" ? local : null,
            At("startedAtActual"),
            parameter is "stoppedAtActual" ? local : null,
            parameter is "abortedAt" ? local : null,
            0,
            resumeCount,
            interruptions,
            parameter is "expectedWindowStart" ? local!.Value : Now.AddMinutes(-5),
            parameter is "expectedWindowEnd" ? local!.Value : Now.AddMinutes(55),
            parameter is "expectedWindowEnd" or "promisedWindowEnd" ? local!.Value : Now.AddMinutes(55),
            null,
            [],
            DropCounters.Unmeasured,
            DropTimeline.Unlocated,
            null,
            0,
            parameter is "measuredUpdatedAt" ? local : null,
            RecordingFactory.Tuner,
            ThumbnailState.Pending,
            RecordingFactory.Snapshot(),
            null,
            BroadcastGroupRole.Standalone);
    }

    [Fact(DisplayName = "BR-PD-009: a recording still being written has no whole file to hand a player, nor a position to keep")]
    public void ARecordingStillBeingWrittenCannotBeWatched()
        => Assert.False(RecordingFactory.Started().CanBeWatched);

    [Theory(DisplayName = "BR-PD-009: a recording that has ended can be watched from its file, however it ended")]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    [InlineData(RecordingOutcome.Failed)]
    public void ARecordingThatHasEndedCanBeWatched(RecordingOutcome outcome)
    {
        Recording recording = RecordingFactory.Started();

        if (outcome is RecordingOutcome.Complete)
        {
            recording.Abort(RecordingFactory.Now.AddHours(1));
        }
        else
        {
            recording.Note(RecordingFactory.Fault());
        }

        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000_000, RecordingFactory.Now.AddHours(1));

        Assert.True(recording.CanBeWatched);
    }

    [Fact(DisplayName = "a recording that says nothing about encoding asks for one")]
    public void ARecordingThatSaysNothingAsksForAnEncode() => Assert.True(Begun().EncodeWhenRecorded);

    [Fact(DisplayName = "a recording carries what the reservation it was started for asked, because nothing ties the two rows together afterwards")]
    public void ARecordingCarriesWhatItWasStartedWith()
        => Assert.False(Begun(encodeWhenRecorded: false).EncodeWhenRecorded);

    private static Recording Begun(bool encodeWhenRecorded = true)
    {
        var id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32736), new ServiceId(1024), new EventId(4001), Noon.AddHours(2)),
            new OutputRoot("primary"),
            RecordingFileName.For(id, ".m2ts"),
            Noon.AddHours(2),
            Noon.AddHours(3),
            Snapshot(),
            null,
            BroadcastGroupRole.Standalone,
            Noon.AddHours(2),
            null,
            encodeWhenRecorded);
    }

    private static ProgrammeSnapshot Snapshot()
        => new(
            "A programme",
            "What it is about",
            string.Empty,
            [],
            Noon,
            AudioMode.Undetermined,
            ProgrammeSnapshot.SoundsUnannounced);

    [Theory]
    [MemberData(nameof(Breaking))]
    public void ARecordingBreaksOnAnythingThatHappensWhileItRuns(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Started();

        recording.Interrupt(fault, Now);

        Assert.Equal([fault], recording.Interruptions.Select(interruption => interruption.Fault).ToArray());
    }

    [Theory]
    [MemberData(nameof(NotBreaking))]
    public void ARecordingDoesNotBreakOnAReasonThatIsNotSomethingHappeningToIt(RecordingFault fault)
    {
        Recording recording = RecordingFactory.Started();

        ArgumentOutOfRangeException refusal =
            Assert.Throws<ArgumentOutOfRangeException>(() => recording.Interrupt(fault, Now));

        Assert.Equal("fault", refusal.ParamName);
    }

    [Fact]
    public void TheFaultsThatBreakARecordingAreTheOnesTheCrossCheckNeverNames()
        => Assert.Empty(RecordingFaults.ThatCanInterrupt.Intersect(CompletionFactory.FaultsTheCrossCheckNames));

    [Fact]
    public void EveryFaultTheLedgerHoldsBreaksARecordingOrConcludesOneOrExplainsItsWindow()
        => Assert.Equal(
            Enum.GetValues<RecordingFault>().Order().ToArray(),
            RecordingFaults.ThatCanInterrupt
                .Concat(CompletionFactory.FaultsTheCrossCheckNames)
                .Concat(RecordingFaults.ThatExplainTheWindow)
                .Order()
                .ToArray());

    [Fact]
    public void NoFaultIsInTwoOfThoseThreeAtOnce()
    {
        Assert.Empty(RecordingFaults.ThatCanInterrupt.Intersect(RecordingFaults.ThatExplainTheWindow));
        Assert.Empty(CompletionFactory.FaultsTheCrossCheckNames.Intersect(RecordingFaults.ThatExplainTheWindow));
    }

    [Fact]
    public void AFaultTheLedgerDoesNotHoldStillBreaksNothing()
    {
        Recording recording = RecordingFactory.Started();

        Assert.Throws<ArgumentOutOfRangeException>(() => recording.Interrupt((RecordingFault)99, Now));
    }

    [Fact]
    public void AReasonMayStillNameSomethingOnlyTheEndingKnows()
    {
        Recording recording = RecordingFactory.Started();

        recording.Note(new OutcomeDetail(RecordingFault.HeavierThanTheStream, null, string.Empty, Now));

        Assert.Equal(
            [RecordingFault.HeavierThanTheStream],
            recording.OutcomeDetail.Select(detail => detail.Fault).ToArray());
    }

    private static TheoryData<RecordingFault> Set(IEnumerable<RecordingFault> faults)
    {
        var named = new TheoryData<RecordingFault>();
        foreach (RecordingFault fault in faults)
        {
            named.Add(fault);
        }

        return named;
    }

    [Fact]
    public void ARecordingCarriedOnAHorizonSaysSoWhileItIsStillRunning()
    {
        Recording recording = Started();

        recording.Note(new OutcomeDetail(RecordingFault.EndStillUndecided, null, string.Empty, Now));

        Assert.Equal(
            RecordingFault.EndStillUndecided,
            Assert.Single(recording.OutcomeDetail).Fault);
    }

    [Fact]
    public void AWindowThatRanOutWhileTheEndWasUndecidedStillSaysSoAfterwards()
    {
        Recording recording = Started();

        recording.Note(new OutcomeDetail(RecordingFault.EndStillUndecided, null, string.Empty, Now));
        recording.Note(new OutcomeDetail(RecordingFault.StoppedUnasked, null, string.Empty, Now));
        recording.Settle(RecordingOutcome.Truncated, 4_000_000L, Now.AddMinutes(50));

        Assert.Equal(RecordingOutcome.Truncated, recording.Outcome);
        Assert.Contains(
            recording.OutcomeDetail,
            detail => detail.Fault is RecordingFault.EndStillUndecided);
    }

    [Fact]
    public void ARecordingTheDriverStoppedOnItsOwnIsNotCalledComplete()
    {
        RecordingVerdict verdict = CompletionEvaluator.Judge(
            new RecordingEvidence(
                4_000_000_000L,
                TimeSpan.FromMinutes(60),
                Now,
                Now.AddMinutes(60),
                null),
            ExpectedBitrate.Of(TunerKind.Terrestrial),
            CompletionTolerance.Default);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Contains(RecordingFault.StoppedUnasked, verdict.Faults);
    }

    [Fact]
    public void AnEndNobodyAnnouncedIsNotSomethingThatBreaksARunningRecording()
        => Assert.DoesNotContain(RecordingFault.EndStillUndecided, RecordingFaults.ThatCanInterrupt);

    [Fact]
    public void AnEndNobodyAnnouncedSaysNothingAboutWhichTunerWasUsed()
        => Assert.DoesNotContain(RecordingFault.EndStillUndecided, RecordingFaults.ThatReachedTheTuner);
}
