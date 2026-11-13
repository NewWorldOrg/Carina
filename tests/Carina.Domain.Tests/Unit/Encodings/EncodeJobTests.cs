using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Encodings;

[Trait("Category", "Unit")]
public sealed class EncodeJobTests
{
    private static readonly DateTime Queued = new(2026, 9, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 4, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Ended = new(2026, 9, 4, 4, 0, 0, DateTimeKind.Utc);

    public static TheoryData<EncodeJobStatus> EveryStatusAJobCanStandAt =>
    [
        EncodeJobStatus.Queued,
        EncodeJobStatus.Running,
        EncodeJobStatus.Completed,
        EncodeJobStatus.Failed,
        EncodeJobStatus.Cancelled,
    ];

    private static readonly OutputRoot Primary = new("primary");

    private static readonly DateTime QueuedForMarks = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime StartedForMarks = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Later = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private static readonly RunningProgramme Ffmpeg = new(4242, StartedForMarks.AddSeconds(1));

    private static readonly EncodeRoute Degraded = new(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach);

    private static readonly DateTime QueuedForRemakes = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime StartedForRemakes = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime QueuedForTracks = new(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAt = new(2026, 10, 4, 2, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime MadeAgainAt = new(2026, 10, 4, 5, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime QueuedForClaims = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime StartedForClaims = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime EndedForClaims = new(2026, 9, 5, 4, 0, 0, DateTimeKind.Utc);

    private static readonly OutputRoot Encodes = new("encodes");

    [Fact(DisplayName = "BR-ES-001: a waiting or running job is called off, and one that has ended answers that it is already over and stays as it was")]
    public void OnlyAJobThatHasNotEndedIsCalledOff()
    {
        EncodeJob waiting = Waiting();
        EncodeJob running = Running();
        EncodeJob completed = Named();
        completed.Complete(Ended);

        Assert.Equal(EncodeCallOff.CalledOff, waiting.CallOff(Ended));
        Assert.Equal(EncodeCallOff.CalledOff, running.CallOff(Ended));
        Assert.Equal(EncodeCallOff.AlreadyOver, completed.CallOff(Ended.AddMinutes(1)));
        Assert.Equal(EncodeCallOff.AlreadyOver, waiting.CallOff(Ended.AddMinutes(1)));
        Assert.Equal(EncodeJobStatus.Cancelled, waiting.Status);
        Assert.Equal(EncodeJobStatus.Cancelled, running.Status);
        Assert.Equal(EncodeJobStatus.Completed, completed.Status);
        Assert.Equal(Ended, waiting.EndedAt);
    }

    [Fact(DisplayName = "a job stands at one of five places and there is no sixth")]
    public void AJobStandsAtOneOfFivePlacesAndThereIsNoSixth()
    {
        Assert.Equal(
            [
                EncodeJobStatus.Queued,
                EncodeJobStatus.Running,
                EncodeJobStatus.Completed,
                EncodeJobStatus.Failed,
                EncodeJobStatus.Cancelled,
            ],
            Enum.GetValues<EncodeJobStatus>());
    }

    [Fact(DisplayName = "what the library is shown is a fifth answer for a recording no job ever touched")]
    public void WhatTheLibraryIsShownHasARoomForARecordingNoJobEverTouched()
    {
        Assert.Equal(
            [
                EncodeStanding.NotEncoded,
                EncodeStanding.Queued,
                EncodeStanding.Running,
                EncodeStanding.Completed,
                EncodeStanding.Failed,
            ],
            Enum.GetValues<EncodeStanding>());

        Assert.Equal(EncodeStanding.NotEncoded, EncodeStandings.Of(null));
    }

    [Fact(DisplayName = "a job that was called off leaves the recording unencoded, not failed")]
    public void AJobThatWasCalledOffLeavesTheRecordingUnencodedRatherThanFailed()
    {
        Assert.Equal(EncodeStanding.NotEncoded, EncodeStandings.Of(EncodeJobStatus.Cancelled));
        Assert.Equal(EncodeStanding.Failed, EncodeStandings.Of(EncodeJobStatus.Failed));
    }

    [Fact(DisplayName = "a job begins waiting, on its first attempt, and has ended nothing")]
    public void AJobBeginsWaitingOnItsFirstAttempt()
    {
        EncodeJob job = Waiting();

        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Equal(EncodeJob.FirstAttempt, job.Attempt);
        Assert.Equal(Queued, job.QueuedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.EndedAt);
        Assert.Null(job.Failure);
        Assert.False(job.HasEnded);
    }

    [Fact(DisplayName = "waiting becomes running, and running becomes finished")]
    public void WaitingBecomesRunningAndRunningBecomesFinished()
    {
        EncodeJob job = Named();

        Assert.Equal(EncodeJobStatus.Running, job.Status);
        Assert.Equal(Started, job.StartedAt);

        job.Complete(Ended);

        Assert.Equal(EncodeJobStatus.Completed, job.Status);
        Assert.Equal(Ended, job.EndedAt);
        Assert.True(job.HasEnded);
    }

    [Fact(DisplayName = "a job that has ended cannot be moved again")]
    public void AJobThatHasEndedCannotBeMovedAgain()
    {
        EncodeJob job = Named();
        job.Complete(Ended);

        Assert.Throws<InvalidOperationException>(() => job.Start(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Complete(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Fail(EncodeFailure.TimedOut, "late", Ended));
        Assert.Throws<InvalidOperationException>(() => job.Cancel(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Requeue(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Artefact(job.RecordingId, job.ProfileId)));
    }

    [Fact(DisplayName = "a job keeps the output root it was queued into, so the destination may move without it")]
    public void AJobKeepsTheOutputRootItWasQueuedInto()
    {
        EncodeJob job = Waiting();

        Assert.Equal(Primary, job.OutputRoot);
        Assert.Null(job.ArtefactName);
    }

    [Fact(DisplayName = "the work file is named for the job and the attempt it is on")]
    public void TheWorkFileIsNamedForTheJobAndTheAttemptItIsOn()
    {
        EncodeJob job = Running();

        Assert.Equal(EncodeFileName.Working(job.RecordingId, job.Id, 1), job.WorkFileName);

        job.Requeue(Ended);

        Assert.Equal(EncodeFileName.Working(job.RecordingId, job.Id, 2), job.WorkFileName);
    }

    [Fact(DisplayName = "the artefact a job makes is the name it will be held to until it names it")]
    public void TheArtefactAJobMakesIsTheNameItWillBeHeldToUntilItNamesIt()
    {
        EncodeJob waiting = Waiting();
        EncodeJob named = Named();

        Assert.Null(waiting.ArtefactName);
        Assert.Equal(EncodeFileName.Artefact(waiting.RecordingId, waiting.ProfileId), waiting.ArtefactItMakes);
        Assert.Equal(named.ArtefactName, named.ArtefactItMakes);
    }

    [Fact(DisplayName = "the artefact is named while the job runs, and only then")]
    public void TheArtefactIsNamedWhileTheJobRunsAndOnlyThen()
    {
        EncodeJob waiting = Waiting();
        EncodeFileName name = EncodeFileName.Artefact(waiting.RecordingId, waiting.ProfileId);

        Assert.Throws<InvalidOperationException>(() => waiting.Name(name));

        EncodeJob running = Running();
        EncodeFileName its = EncodeFileName.Artefact(running.RecordingId, running.ProfileId);
        running.Name(its);

        Assert.Equal(its, running.ArtefactName);
    }

    [Fact(DisplayName = "a job cannot finish without having said what it made")]
    public void AJobCannotFinishWithoutHavingSaidWhatItMade()
    {
        EncodeJob job = Running();

        Assert.Throws<InvalidOperationException>(() => job.Complete(Ended));
        Assert.Equal(EncodeJobStatus.Running, job.Status);
    }

    [Fact(DisplayName = "naming the artefact again by the same name is its own success seen again, not a second claim")]
    public void NamingTheArtefactAgainByTheSameNameIsNotASecondClaim()
    {
        EncodeJob job = Named();
        EncodeFileName same = EncodeFileName.Artefact(job.RecordingId, job.ProfileId);

        job.Name(same);

        Assert.Equal(same, job.ArtefactName);
    }

    [Fact(DisplayName = "a job that has named its artefact cannot be talked into another name")]
    public void AJobThatHasNamedItsArtefactCannotBeTalkedIntoAnotherName()
    {
        EncodeJob job = Named();

        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Artefact(job.RecordingId, EncodeProfileId.New())));
    }

    [Fact(DisplayName = "the name survives a requeue, so the next attempt can recognise its own success")]
    public void TheNameSurvivesARequeue()
    {
        EncodeJob job = Named();
        EncodeFileName named = job.ArtefactName!;

        job.Requeue(Ended);

        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Equal(named, job.ArtefactName);
    }

    [Fact(DisplayName = "the artefact is named for the recording and the profile the job was queued with")]
    public void TheArtefactIsNamedForTheRecordingAndTheProfileTheJobWasQueuedWith()
    {
        EncodeJob job = Running();

        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Artefact(RecordingId.New(), job.ProfileId)));
        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Working(job.RecordingId, job.Id, 1)));
    }

    [Fact(DisplayName = "a job that is still waiting cannot finish without having run")]
    public void AJobThatIsStillWaitingCannotFinishWithoutHavingRun()
    {
        EncodeJob job = Waiting();

        Assert.Throws<InvalidOperationException>(() => job.Complete(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Fail(EncodeFailure.SourceMissing, "gone", Ended));
        Assert.Throws<InvalidOperationException>(() => job.Requeue(Ended));
    }

    [Fact(DisplayName = "a job picked up again is put back to waiting with its attempt counted")]
    public void AJobPickedUpAgainIsPutBackToWaitingWithItsAttemptCounted()
    {
        EncodeJob job = Running();

        job.Requeue(Ended);

        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Equal(EncodeJob.FirstAttempt + 1, job.Attempt);
        Assert.Equal(Ended, job.QueuedAt);
        Assert.Null(job.StartedAt);
    }

    [Fact(DisplayName = "a job found running when the process comes up goes back to the queue with its attempt counted")]
    public void AJobFoundRunningWhenTheProcessComesUpGoesBackToTheQueue()
    {
        EncodeJob job = Running();

        EncodeRecovery recovery = job.Recover(3, Ended);

        Assert.Equal(EncodeRecovery.PutBack, recovery);
        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Equal(EncodeJob.FirstAttempt + 1, job.Attempt);
        Assert.Equal(Ended, job.QueuedAt);
        Assert.Null(job.StartedAt);
        Assert.Null(job.Failure);
    }

    [Fact(DisplayName = "a job on its last attempt when the process comes up is given up as timed out, not put back")]
    public void AJobOnItsLastAttemptWhenTheProcessComesUpIsGivenUp()
    {
        EncodeJob job = EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            EncodeJobStatus.Running,
            3,
            Queued,
            Started,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);

        EncodeRecovery recovery = job.Recover(3, Ended);

        Assert.Equal(EncodeRecovery.GivenUp, recovery);
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.Equal(3, job.Attempt);
        Assert.Equal(EncodeFailure.TimedOut, job.Failure!.Failure);
        Assert.Contains("attempt 3 of the 3", job.Failure.Note, StringComparison.Ordinal);
        Assert.Equal(Ended, job.EndedAt);
    }

    [Fact(DisplayName = "only a job the ledger holds as running is picked up again, and it gets at least one attempt")]
    public void OnlyARunningJobIsPickedUpAgain()
    {
        Assert.Throws<InvalidOperationException>(() => Waiting().Recover(3, Ended));
        Assert.Throws<ArgumentOutOfRangeException>(() => Running().Recover(0, Ended));
    }

    [Fact(DisplayName = "a claim carries a job only when the ledger holds it as running")]
    public void AClaimCarriesAJobOnlyWhenTheLedgerHoldsItAsRunning()
    {
        EncodeClaim claimed = EncodeClaim.Of(Running());

        Assert.Equal(EncodeClaimStanding.Claimed, claimed.Standing);
        Assert.NotNull(claimed.Job);
        Assert.Throws<ArgumentException>(() => EncodeClaim.Of(Waiting()));
        Assert.Null(EncodeClaim.NothingWaiting().Job);
        Assert.Equal(EncodeClaimStanding.AnotherIsRunning, EncodeClaim.AnotherIsRunning().Standing);
        Assert.Equal(EncodeClaimStanding.TakenMeanwhile, EncodeClaim.TakenMeanwhile().Standing);
    }

    [Fact(DisplayName = "a programme reported as begun before the job began is written down as begun when the job began, under its own id; one begun after is written as reported")]
    public void AProgrammeReportedAsBegunBeforeTheJobIsWrittenDownAsBegunWithTheJob()
    {
        EncodeJob early = Running();
        EncodeJob late = Running();

        early.Spawned(new RunningProgramme(4242, Started.AddMilliseconds(-400)));
        late.Spawned(new RunningProgramme(4343, Started.AddMilliseconds(400)));

        Assert.Equal(4242, early.Programme!.ProcessId);
        Assert.Equal(Started, early.Programme.StartedAt);
        Assert.Equal(4343, late.Programme!.ProcessId);
        Assert.Equal(Started.AddMilliseconds(400), late.Programme.StartedAt);
        Assert.Throws<InvalidOperationException>(() => Waiting().Spawned(new RunningProgramme(4242, Started)));
        Assert.Throws<ArgumentNullException>(() => Running().Spawned(null!));
    }

    [Fact(DisplayName = "a failure is a classification, and the words beside it are not the reason")]
    public void AFailureIsAClassificationAndTheWordsBesideItAreNotTheReason()
    {
        EncodeJob job = Running();

        job.Fail(EncodeFailure.NotEnoughRoom, "  no space left on device  ", Ended);

        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.NotNull(job.Failure);
        Assert.Equal(EncodeFailure.NotEnoughRoom, job.Failure!.Failure);
        Assert.Equal("no space left on device", job.Failure.Note);
        Assert.Equal(Ended, job.Failure.NoticedAt);
    }

    [Fact(DisplayName = "the eight reasons a job fails for are these and no other")]
    public void TheEightReasonsAJobFailsForAreTheseAndNoOther()
    {
        Assert.Equal(
            [
                EncodeFailure.FfmpegExitedNonZero,
                EncodeFailure.NotEnoughRoom,
                EncodeFailure.SourceMissing,
                EncodeFailure.CapabilityUnavailable,
                EncodeFailure.TimedOut,
                EncodeFailure.DestinationCollision,
                EncodeFailure.HeadTooFar,
                EncodeFailure.EndingNotKept,
            ],
            Enum.GetValues<EncodeFailure>());
    }

    [Fact(DisplayName = "a running job is aligned once before its run and measured once after it, and the two together say where its clock stands")]
    public void ARunningJobIsAlignedBeforeItsRunAndMeasuredAfterIt()
    {
        EncodeJob job = Running();
        var timeline = new EncodeTimeline(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(0.5072), TimeSpan.FromSeconds(2097.502489), null);

        job.Aligned(timeline);
        job.Measured(TimeSpan.FromSeconds(2096.795));

        Assert.Equal(TimeSpan.FromSeconds(0.5072), job.Timeline!.HeadSkip);
        Assert.Equal(TimeSpan.FromSeconds(30499.981278), job.Timeline.CaptionShift);
        Assert.Equal(TimeSpan.FromSeconds(2096.795), job.Timeline.ArtefactLength);
        Assert.True(job.Timeline.LengthsAgree);
    }

    [Fact(DisplayName = "only a running job is aligned or measured, a job is measured against the clock it was aligned to, and a job put back in the queue starts over unaligned")]
    public void OnlyARunningJobIsAlignedOrMeasuredAndARequeuedJobStartsOverUnaligned()
    {
        var timeline = new EncodeTimeline(TimeSpan.FromSeconds(30499.474078), TimeSpan.FromSeconds(0.5072), null, null);
        EncodeJob waiting = Waiting();
        EncodeJob unaligned = Running();
        EncodeJob aligned = Running();
        aligned.Aligned(timeline);

        Assert.Throws<InvalidOperationException>(() => waiting.Aligned(timeline));
        Assert.Throws<InvalidOperationException>(() => unaligned.Measured(TimeSpan.FromSeconds(10)));
        Assert.Throws<ArgumentNullException>(() => unaligned.Aligned(null!));

        aligned.Requeue(Ended);

        Assert.Null(aligned.Timeline);
        Assert.Throws<ArgumentException>(() => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            EncodeJobStatus.Queued,
            1,
            Queued,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            timeline,
            null));
    }

    [Fact(DisplayName = "a reason nobody named cannot be recorded as one")]
    public void AReasonNobodyNamedCannotBeRecordedAsOne()
    {
        EncodeJob job = Running();

        Assert.Throws<ArgumentOutOfRangeException>(() => job.Fail((EncodeFailure)99, "who knows", Ended));
    }

    [Fact(DisplayName = "only the tail of what the programme said is kept")]
    public void OnlyTheTailOfWhatTheProgrammeSaidIsKept()
    {
        string said = new string('a', EncodeNote.Longest) + "the part that matters";

        Assert.Equal(EncodeNote.Longest, EncodeNote.Of(said).Length);
        Assert.EndsWith("the part that matters", EncodeNote.Of(said), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "calling a job off is a different move from failing it")]
    public void CallingAJobOffIsADifferentMoveFromFailingIt()
    {
        EncodeJob calledOff = Running();
        calledOff.Cancel(Ended);

        Assert.Equal(EncodeJobStatus.Cancelled, calledOff.Status);
        Assert.Null(calledOff.Failure);

        EncodeJob waiting = Waiting();
        waiting.Cancel(Ended);

        Assert.Equal(EncodeJobStatus.Cancelled, waiting.Status);
    }

    [Theory]
    [MemberData(nameof(EveryStatusAJobCanStandAt))]
    public void EveryStatusHasAnAnswerForTheLibrary(EncodeJobStatus status)
        => Assert.True(Enum.IsDefined(EncodeStandings.Of(status)));

    [Fact]
    public void AJobCannotBeMadeWithoutGoingThroughTheOneWayIn()
        => Assert.Empty(typeof(EncodeJob).GetConstructors());

    [Fact(DisplayName = "a job queued the ordinary way makes nothing again, and one queued because a person asked says so from the moment it is queued")]
    public void AJobQueuedTheOrdinaryWayMakesNothingAgain()
    {
        EncodeJob ordinary = Waiting();
        EncodeJob again = EncodeJob.QueueAgain(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            Queued);

        Assert.False(ordinary.MakesItAgain);
        Assert.Null(ordinary.NameGivenUpAt);
        Assert.True(again.MakesItAgain);
        Assert.Null(again.NameGivenUpAt);
        Assert.Equal(EncodeJobStatus.Queued, again.Status);
        Assert.Equal(EncodeJob.FirstAttempt, again.Attempt);
        Assert.Null(again.ArtefactName);
    }

    [Fact(DisplayName = "a job gives up only a name it holds, and still names what it made afterwards")]
    public void AJobGivesUpOnlyANameItHolds()
    {
        EncodeJob job = Named();
        EncodeFileName name = job.ArtefactName!;

        Assert.Throws<InvalidOperationException>(() => Waiting().GiveUpTheName(Ended));

        job.GiveUpTheName(Ended);

        Assert.Equal(Ended, job.NameGivenUpAt);
        Assert.Equal(name, job.ArtefactName);
    }

    [Fact(DisplayName = "a job that fails or is called off lets go of the name it held, and still says what it was called")]
    public void AJobThatDoesNotCompleteLetsGoOfTheNameItHeld()
    {
        EncodeJob failed = Named();
        EncodeJob cancelled = Named();

        failed.Fail(EncodeFailure.NotEnoughRoom, "the disk is full", Ended);
        cancelled.Cancel(Ended);

        Assert.Equal(Ended, failed.NameGivenUpAt);
        Assert.Equal(Ended, cancelled.NameGivenUpAt);
        Assert.Equal(EncodeFileName.Artefact(failed.RecordingId, failed.ProfileId), failed.ArtefactName);
    }

    [Fact(DisplayName = "a job that named nothing has no name to let go of when it fails or is called off")]
    public void AJobThatNamedNothingHasNoNameToLetGoOf()
    {
        EncodeJob failed = Running();
        EncodeJob cancelled = Waiting();

        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "the programme exited 1", Ended);
        cancelled.Cancel(Ended);

        Assert.Null(failed.NameGivenUpAt);
        Assert.Null(cancelled.NameGivenUpAt);
    }

    [Fact(DisplayName = "a name already given up keeps the moment it was given up when the job then fails")]
    public void ANameAlreadyGivenUpKeepsTheMomentItWasGivenUp()
    {
        EncodeJob job = Named();
        job.GiveUpTheName(Started);

        job.Fail(EncodeFailure.CapabilityUnavailable, "the rename was refused", Ended);

        Assert.Equal(Started, job.NameGivenUpAt);
    }

    [Fact(DisplayName = "a job that completes keeps the name it holds")]
    public void AJobThatCompletesKeepsTheNameItHolds()
    {
        EncodeJob job = Named();

        job.Complete(Ended);

        Assert.Null(job.NameGivenUpAt);
    }

    [Fact(DisplayName = "a job that named nothing cannot be read back as having given a name up")]
    public void AJobThatNamedNothingCannotHaveGivenANameUp()
        => Assert.Throws<ArgumentException>(() => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            EncodeJobStatus.Running,
            1,
            Queued,
            Started,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            true,
            Ended));

    [Fact]
    public void AnAttemptBeforeTheFirstIsNotAnAttempt()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            EncodeJobStatus.Queued,
            0,
            Queued,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null));

    [Fact(DisplayName = "BR-CD-010: the chapters of an artefact that stands are written down again as the segments' once compared after the job ended, and of no other job")]
    public void TheChaptersOfAStandingArtefactAreWrittenDownAgain()
    {
        EncodeJob job = Named();
        job.Judged(new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 0.2, Started));
        job.Complete(Ended);
        ChapterReading again = new(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.1, Ended.AddHours(1), 2);

        job.Rechaptered(again);

        Assert.Equal(again, job.Chapters);
        Assert.Throws<ArgumentOutOfRangeException>(() => job.Rechaptered(new ChapterReading(ChapterDetectorName.Segments, ChapterVerdict.Marked, 0.1, Ended.AddSeconds(-1), 2)));
        Assert.Throws<ArgumentException>(() => job.Rechaptered(new ChapterReading(ChapterDetectorName.Ffmpeg, ChapterVerdict.Marked, 0.1, Ended.AddHours(1))));
        Assert.Throws<InvalidOperationException>(() => Running().Rechaptered(again));
    }

    [Fact(DisplayName = "where a run went is written on the job, so a degraded run is in the ledger and not only in a log")]
    public void WhereARunWentIsWrittenOnTheJob()
    {
        EncodeJob job = RunningForMarks();

        job.Routed(Degraded);

        Assert.Equal(EncodeEncoder.Vaapi, job.Route!.Asked);
        Assert.Equal(EncodeEncoder.Software, job.Route.Ran);
        Assert.Equal(EncodeSwerve.TheCardIsOutOfReach, job.Route.Swerved);
        Assert.True(job.Route.WasDegraded);
    }

    [Fact(DisplayName = "the programme a run started is written on the job as its id and when it began, together")]
    public void TheProgrammeARunStartedIsWrittenOnTheJob()
    {
        EncodeJob job = RunningForMarks();

        job.Spawned(Ffmpeg);

        Assert.Equal(4242, job.Programme!.ProcessId);
        Assert.Equal(StartedForMarks.AddSeconds(1), job.Programme.StartedAt);
    }

    [Fact(DisplayName = "headway is written on the job as the portion done, what is left and when that was reported")]
    public void HeadwayIsWrittenOnTheJobWithWhenItWasReported()
    {
        EncodeJob job = RunningForMarks();

        job.Reached(EncodeProgress.Of(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), 2, false), Later);

        Assert.Equal(0.5, job.Headway!.Portion);
        Assert.Equal(TimeSpan.FromSeconds(15), job.Headway.Left);
        Assert.Equal(Later, job.Headway.At);
    }

    [Fact(DisplayName = "a running job is quiet for as long as it has been since its last headway, and before any headway since it started")]
    public void ARunningJobIsQuietForAsLongAsSinceItsLastHeadway()
    {
        EncodeJob fresh = RunningForMarks();
        EncodeJob heard = Marked();

        Assert.Equal(Later - StartedForMarks, fresh.QuietFor(Later));
        Assert.Equal(TimeSpan.Zero, heard.QuietFor(Later));
        Assert.Equal(TimeSpan.FromMinutes(5), heard.QuietFor(Later.AddMinutes(5)));
        Assert.Null(WaitingForMarks().QuietFor(Later));
    }

    [Fact(DisplayName = "a running job that has made no headway for as long as a run may go quiet is stalled, and the ledger's 'running' is not to be read as such")]
    public void ARunningJobWithNoHeadwayForTooLongIsStalled()
    {
        EncodeJob job = Marked();

        Assert.False(job.IsStalled(Later.AddMinutes(9), TenMinutes));
        Assert.True(job.IsStalled(Later.AddMinutes(10), TenMinutes));
        Assert.True(job.IsStalled(Later.AddHours(3), TenMinutes));
    }

    [Fact(DisplayName = "a job that has yet to report anything is stalled from its start, not never")]
    public void AJobThatHasReportedNothingIsStalledFromItsStart()
    {
        EncodeJob job = RunningForMarks();

        Assert.False(job.IsStalled(StartedForMarks.AddMinutes(9), TenMinutes));
        Assert.True(job.IsStalled(StartedForMarks.AddMinutes(10), TenMinutes));
    }

    [Fact(DisplayName = "only a running job can be stalled; a job that ended quiet is not, and a job cannot be asked about a quiet of no length")]
    public void OnlyARunningJobCanBeStalled()
    {
        EncodeJob ended = Marked();
        ended.Fail(EncodeFailure.TimedOut, "quiet", Later.AddHours(1));

        Assert.False(ended.IsStalled(Later.AddHours(3), TenMinutes));
        Assert.False(WaitingForMarks().IsStalled(Later.AddHours(3), TenMinutes));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunningForMarks().IsStalled(Later, TimeSpan.Zero));
    }

    [Fact(DisplayName = "a job put back in the queue carries nothing of the run it was on: no route, no programme, no headway")]
    public void AJobPutBackCarriesNothingOfTheRunItWasOn()
    {
        EncodeJob job = Marked();

        job.Requeue(Later);

        Assert.Null(job.Route);
        Assert.Null(job.Programme);
        Assert.Null(job.Headway);
    }

    [Fact(DisplayName = "a job that ends keeps where it ran and how far it got, and lets go of the programme, which has exited")]
    public void AJobThatEndsKeepsTheRouteAndTheHeadwayAndLetsGoOfTheProgramme()
    {
        EncodeJob failed = Marked();
        EncodeJob completed = Marked();
        EncodeJob cancelled = Marked();
        completed.Name(EncodeFileName.Artefact(completed.RecordingId, completed.ProfileId));

        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "refused", Later);
        completed.Complete(Later);
        cancelled.Cancel(Later);

        foreach (EncodeJob job in new[] { failed, completed, cancelled })
        {
            Assert.Null(job.Programme);
            Assert.Same(Degraded, job.Route);
            Assert.NotNull(job.Headway);
        }
    }

    [Fact(DisplayName = "only a running job runs somewhere, has a programme, or makes headway")]
    public void OnlyARunningJobIsMarked()
    {
        EncodeJob waiting = WaitingForMarks();
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.Zero, null, 0, false);

        Assert.Throws<InvalidOperationException>(() => waiting.Routed(Degraded));
        Assert.Throws<InvalidOperationException>(() => waiting.Spawned(Ffmpeg));
        Assert.Throws<InvalidOperationException>(() => waiting.Reached(progress, Later));
    }

    [Fact(DisplayName = "a row that says a waiting job has a programme, or ran somewhere, is not one the ledger can hold")]
    public void ARowThatSaysAWaitingJobHasAProgrammeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Queued, null, programme: Ffmpeg));
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Queued, null, route: Degraded));
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Completed, Later, programme: Ffmpeg));
        Assert.NotNull(Rehydrated(EncodeJobStatus.Running, null, route: Degraded, programme: Ffmpeg).Programme);
    }

    [Fact(DisplayName = "a job makes nothing again until a run says what it made outgrew its recording")]
    public void AJobMakesNothingAgainUntilARunSaysSo()
    {
        Assert.Null(WaitingForRemakes().Remade);
        Assert.Null(RunningForRemakes().Remade);
    }

    [Fact(DisplayName = "a running job whose artefact outgrew its recording writes down that it makes it again, and why")]
    public void ARunningJobWritesDownThatItMakesItAgain()
    {
        EncodeJob job = RunningForRemakes();

        job.Remaking();

        Assert.Equal(EncodeRemake.LargerThanTheSource, job.Remade);
    }

    [Fact(DisplayName = "a job makes its artefact again once, and only while it runs")]
    public void AJobMakesItsArtefactAgainOnceAndOnlyWhileItRuns()
    {
        EncodeJob job = RunningForRemakes();
        job.Remaking();

        Assert.Throws<InvalidOperationException>(job.Remaking);
        Assert.Throws<InvalidOperationException>(WaitingForRemakes().Remaking);
    }

    [Fact(DisplayName = "a job that made its artefact again says what it kept once, and only after it began making it again")]
    public void AJobThatMadeItsArtefactAgainSaysWhatItKept()
    {
        EncodeJob job = RunningForRemakes();

        Assert.Throws<InvalidOperationException>(() => job.MadeAgain(EncodeRemake.StillLargerThanTheSource));

        job.Remaking();
        job.MadeAgain(EncodeRemake.StillLargerThanTheSource);

        Assert.Equal(EncodeRemake.StillLargerThanTheSource, job.Remade);
        Assert.Throws<InvalidOperationException>(() => job.MadeAgain(EncodeRemake.NoSmallerTheSecondTime));
        Assert.Throws<ArgumentOutOfRangeException>(() => RunningForRemakes().MadeAgain((EncodeRemake)99));
    }

    [Fact(DisplayName = "the work file a job keeps is the first until it makes its artefact again, the second while it does and once it kept that, and the first once the second came out no smaller")]
    public void TheWorkFileAJobKeepsFollowsWhatItKept()
    {
        EncodeJob once = RunningForRemakes();
        EncodeJob smaller = RunningForRemakes();
        smaller.Remaking();
        EncodeJob still = RunningForRemakes();
        still.Remaking();
        still.MadeAgain(EncodeRemake.StillLargerThanTheSource);
        EncodeJob first = RunningForRemakes();
        first.Remaking();
        first.MadeAgain(EncodeRemake.NoSmallerTheSecondTime);

        Assert.Equal(once.WorkFileName, once.KeptWorkFileName);
        Assert.Equal(smaller.RemadeFileName, smaller.KeptWorkFileName);
        Assert.Equal(still.RemadeFileName, still.KeptWorkFileName);
        Assert.Equal(first.WorkFileName, first.KeptWorkFileName);
        Assert.Equal(first.WorkFileName, first.FileToPlace);
        Assert.Equal(smaller.RemadeFileName, smaller.FileToPlace);
    }

    [Fact(DisplayName = "the second work file is named for the recording, the job and the attempt, apart from the first")]
    public void TheSecondWorkFileIsNamedApartFromTheFirst()
    {
        EncodeJob job = RunningForRemakes();

        Assert.NotEqual(job.WorkFileName, job.RemadeFileName);
        Assert.True(job.RemadeFileName.Names(job.RecordingId));
        Assert.True(job.RemadeFileName.Names(job.Id));
        Assert.EndsWith($".attempt{job.Attempt}.remade{EncodeFileName.WorkExtension}", job.RemadeFileName.Value, StringComparison.Ordinal);
    }

    [Fact(DisplayName = "what a job made again is kept when it ends, and forgotten when it goes back to the queue to start over")]
    public void WhatAJobMadeAgainIsForgottenWhenItStartsOver()
    {
        EncodeJob ended = RunningForRemakes();
        ended.Remaking();
        ended.Name(EncodeFileName.Artefact(ended.RecordingId, ended.ProfileId));
        ended.Complete(Later);

        EncodeJob putBack = RunningForRemakes();
        putBack.Remaking();
        putBack.Requeue(Later);

        Assert.Equal(EncodeRemake.LargerThanTheSource, ended.Remade);
        Assert.Null(putBack.Remade);
    }

    [Fact(DisplayName = "a row that says a waiting job made something again, or names no reason the ledger knows, is not one the ledger can hold")]
    public void ARowThatSaysAWaitingJobMadeSomethingAgainIsRefused()
    {
        Assert.Throws<ArgumentException>(() => RehydratedForRemakes(EncodeJobStatus.Queued, EncodeRemake.LargerThanTheSource));
        Assert.Throws<ArgumentOutOfRangeException>(() => RehydratedForRemakes(EncodeJobStatus.Running, (EncodeRemake)99));
        Assert.Equal(EncodeRemake.NoSmallerTheSecondTime, RehydratedForRemakes(EncodeJobStatus.Running, EncodeRemake.NoSmallerTheSecondTime).Remade);
    }

    [Fact]
    public void BrEd2019AnArtefactThatStandsAwaitsATrackFromARecordNoTrackWasTriedFrom()
    {
        EncodeJob job = Completed();

        Assert.True(job.AwaitsCaptionTrack(MadeAt));

        job.Tracked(EncodeCaptionTrack.Added, MadeAt);

        Assert.False(job.AwaitsCaptionTrack(MadeAt));
        Assert.True(job.AwaitsCaptionTrack(MadeAgainAt));
        Assert.Equal((EncodeCaptionTrack.Added, MadeAt, 0), (job.CaptionTrack!.Value, job.CaptionTrackFrom!.Value, job.CaptionTrackAttempts));
    }

    [Fact]
    public void BrEd2019ATrackWithheldFromARecordIsNotTriedAgainFromTheSameRecord()
    {
        EncodeJob job = Completed();

        job.Tracked(EncodeCaptionTrack.Withheld, MadeAt);

        Assert.False(job.AwaitsCaptionTrack(MadeAt));
    }

    [Fact]
    public void BrEd2019AFailedTrackIsTriedThreeTimesFromTheSameRecordAndCountedAgainFromANewOne()
    {
        EncodeJob job = Completed();

        job.Tracked(EncodeCaptionTrack.Failed, MadeAt);
        job.Tracked(EncodeCaptionTrack.Failed, MadeAt);

        Assert.True(job.AwaitsCaptionTrack(MadeAt));

        job.Tracked(EncodeCaptionTrack.Failed, MadeAt);

        Assert.Equal(EncodeJob.CaptionTrackTriesAtMost, job.CaptionTrackAttempts);
        Assert.False(job.AwaitsCaptionTrack(MadeAt));

        job.Tracked(EncodeCaptionTrack.Failed, MadeAgainAt);

        Assert.Equal(1, job.CaptionTrackAttempts);
        Assert.True(job.AwaitsCaptionTrack(MadeAgainAt));
    }

    [Fact]
    public void BrEd2019ARunningJobWritesDownTheTrackOfTheArtefactItIsAboutToPlace()
    {
        EncodeJob job = RunningForTracks();

        job.Tracked(EncodeCaptionTrack.Added, MadeAt);

        Assert.Equal(EncodeCaptionTrack.Added, job.CaptionTrack);
        Assert.False(job.AwaitsCaptionTrack(MadeAt));
    }

    [Fact]
    public void BrEd2019AJobWhoseArtefactDoesNotStandHasNoTrackPutIn()
    {
        EncodeJob replaced = Completed();
        EncodeJob newer = Completed(replaced.RecordingId);
        replaced.Replaced(newer, QueuedForTracks.AddHours(3));
        EncodeJob failed = RunningForTracks();
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "it broke", QueuedForTracks.AddHours(1));
        EncodeJob waiting = WaitingForTracks();

        Assert.False(replaced.AwaitsCaptionTrack(MadeAt));
        Assert.Throws<InvalidOperationException>(() => replaced.Tracked(EncodeCaptionTrack.Added, MadeAt));
        Assert.Throws<InvalidOperationException>(() => failed.Tracked(EncodeCaptionTrack.Added, MadeAt));
        Assert.Throws<InvalidOperationException>(() => waiting.Tracked(EncodeCaptionTrack.Added, MadeAt));
    }

    [Fact]
    public void ATrackIsRehydratedWithWhatBecameOfItAndTheRecordItCameFromTogether()
    {
        EncodeJob job = Completed();

        Assert.Throws<ArgumentException>(() => RehydratedForTracks(job, EncodeCaptionTrack.Added, null, 0));
        Assert.Throws<ArgumentException>(() => RehydratedForTracks(job, null, MadeAt, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => RehydratedForTracks(job, EncodeCaptionTrack.Added, MadeAt, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RehydratedForTracks(job, EncodeCaptionTrack.Failed, MadeAt, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => RehydratedForTracks(job, (EncodeCaptionTrack)9, MadeAt, 0));
        Assert.Equal(2, RehydratedForTracks(job, EncodeCaptionTrack.Failed, MadeAt, 2).CaptionTrackAttempts);
    }

    [Fact]
    public void BrEd2019TheFilesATrackIsMadeWithAreNamedForTheRecordingTheJobAndTheAttempt()
    {
        EncodeJob job = RunningForTracks();

        Assert.Equal($"{job.RecordingId.Wire}.{job.Id.Wire}.attempt1.vtt", job.CaptionTrackFileName.Value);
        Assert.Equal($"{job.RecordingId.Wire}.{job.Id.Wire}.attempt1.captioned", job.CaptionedFileName.Value);
    }

    [Fact]
    public void BrEd2019AnArtefactThatStandsIsTriedUnderNamesOfTheTrySoNoTwoTriesShareOne()
    {
        EncodeJob job = Completed();
        EncodeScratchFileId tried = EncodeScratchFileId.New();

        Assert.Equal($"{job.RecordingId.Wire}.{job.Id.Wire}.attempt1.try{tried.Value:N}.vtt", EncodeFileName.CaptionTrack(job.RecordingId, job.Id, 1, tried).Value);
        Assert.Equal($"{job.RecordingId.Wire}.{job.Id.Wire}.attempt1.try{tried.Value:N}.captioned", EncodeFileName.Captioned(job.RecordingId, job.Id, 1, tried).Value);
        Assert.NotEqual(EncodeFileName.Captioned(job.RecordingId, job.Id, 1, tried), EncodeFileName.Captioned(job.RecordingId, job.Id, 1, EncodeScratchFileId.New()));
    }

    [Theory(DisplayName = "BR-KD-025: a job still in hand claims the artefact it makes, whether or not the recording is still held")]
    [InlineData(true)]
    [InlineData(false)]
    public void AJobInHandClaimsItsArtefact(bool recordingHeld)
    {
        Assert.True(Queue().ClaimsItsArtefact(recordingHeld));
        Assert.True(RunningForClaims().ClaimsItsArtefact(recordingHeld));
    }

    [Fact(DisplayName = "BR-KD-025: a completed job claims its artefact only while it stands for a recording the ledger still holds")]
    public void ACompletedJobClaimsItsArtefactWhileItStandsForARecordingHeld()
    {
        EncodeJob replaced = CompletedForClaims(EndedForClaims);
        replaced.Replaced(CompletedForClaims(EndedForClaims.AddMinutes(1), replaced.RecordingId), EndedForClaims.AddMinutes(2));

        Assert.True(CompletedForClaims(EndedForClaims).ClaimsItsArtefact(true));
        Assert.False(CompletedForClaims(EndedForClaims).ClaimsItsArtefact(false));
        Assert.False(replaced.ClaimsItsArtefact(true));
    }

    [Fact(DisplayName = "BR-KD-025: a job that failed or was called off claims no artefact")]
    public void AJobThatDidNotCompleteClaimsNoArtefact()
    {
        EncodeJob failed = RunningForClaims();
        failed.Fail(EncodeFailure.FfmpegExitedNonZero, "exit 1", EndedForClaims);
        EncodeJob cancelled = RunningForClaims();
        cancelled.Cancel(EndedForClaims);

        Assert.False(failed.ClaimsItsArtefact(true));
        Assert.False(cancelled.ClaimsItsArtefact(true));
    }

    private static EncodeJob Waiting()
        => EncodeJob.Queue(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            Queued);

    private static EncodeJob Named()
    {
        EncodeJob job = Running();
        job.Name(EncodeFileName.Artefact(job.RecordingId, job.ProfileId));

        return job;
    }

    private static EncodeJob Running()
    {
        EncodeJob job = Waiting();
        job.Start(Started);

        return job;
    }

    private static EncodeJob WaitingForMarks()
        => EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), Primary, QueuedForMarks);

    private static EncodeJob RunningForMarks()
    {
        EncodeJob job = WaitingForMarks();
        job.Start(StartedForMarks);

        return job;
    }

    private static EncodeJob Marked()
    {
        EncodeJob job = RunningForMarks();
        job.Routed(Degraded);
        job.Spawned(Ffmpeg);
        job.Reached(EncodeProgress.Of(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), 2, false), Later);

        return job;
    }

    private static EncodeJob Rehydrated(
        EncodeJobStatus status,
        DateTime? ended,
        EncodeRoute? route = null,
        RunningProgramme? programme = null)
    {
        var recording = RecordingId.New();
        var profile = EncodeProfileId.New();

        return EncodeJob.Rehydrate(
            EncodeJobId.New(),
            recording,
            profile,
            EncodeDestinationId.New(),
            Primary,
            status,
            1,
            QueuedForMarks,
            status is EncodeJobStatus.Queued ? null : StartedForMarks,
            ended,
            null,
            status is EncodeJobStatus.Completed ? EncodeFileName.Artefact(recording, profile) : null,
            route,
            programme,
            null,
            null,
            null);
    }

    private static EncodeJob WaitingForRemakes()
        => EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), Primary, QueuedForRemakes);

    private static EncodeJob RunningForRemakes()
    {
        EncodeJob job = WaitingForRemakes();
        job.Start(StartedForRemakes);

        return job;
    }

    private static EncodeJob RehydratedForRemakes(EncodeJobStatus status, EncodeRemake remade)
        => EncodeJob.Rehydrate(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Primary,
            status,
            EncodeJob.FirstAttempt,
            QueuedForRemakes,
            status is EncodeJobStatus.Queued ? null : StartedForRemakes,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            remade: remade);

    private static EncodeJob RehydratedForTracks(EncodeJob job, EncodeCaptionTrack? track, DateTime? from, int attempts)
        => EncodeJob.Rehydrate(
            job.Id,
            job.RecordingId,
            job.ProfileId,
            job.DestinationId,
            job.OutputRoot,
            job.Status,
            job.Attempt,
            job.QueuedAt,
            job.StartedAt,
            job.EndedAt,
            job.Failure,
            job.ArtefactName,
            job.Route,
            job.Programme,
            job.Headway,
            job.Timeline,
            job.Chapters,
            job.MakesItAgain,
            job.NameGivenUpAt,
            job.ReplacedAt,
            track,
            from,
            attempts);

    private static EncodeJob WaitingForTracks(RecordingId? recording = null)
        => EncodeJob.Queue(EncodeJobId.New(), recording ?? RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), Primary, QueuedForTracks);

    private static EncodeJob RunningForTracks(RecordingId? recording = null)
    {
        EncodeJob job = WaitingForTracks(recording);
        job.Start(QueuedForTracks.AddSeconds(5));

        return job;
    }

    private static EncodeJob Completed(RecordingId? recording = null)
    {
        EncodeJob job = RunningForTracks(recording);
        job.Name(EncodeFileName.Artefact(job.RecordingId, job.ProfileId));
        job.Complete(QueuedForTracks.AddHours(1));

        return job;
    }

    private static EncodeJob Queue()
        => EncodeJob.QueueAgain(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Encodes,
            QueuedForClaims);

    private static EncodeJob RunningForClaims()
    {
        EncodeJob job = Queue();
        job.Start(StartedForClaims);

        return job;
    }

    private static EncodeJob CompletedForClaims(DateTime ended, RecordingId? recording = null)
    {
        RecordingId of = recording ?? RecordingId.New();
        EncodeProfileId made = EncodeProfileId.New();

        return EncodeJob.Rehydrate(
            EncodeJobId.New(),
            of,
            made,
            EncodeDestinationId.New(),
            Encodes,
            EncodeJobStatus.Completed,
            EncodeJob.FirstAttempt,
            QueuedForClaims,
            StartedForClaims,
            ended,
            null,
            EncodeFileName.Artefact(of, made),
            null,
            null,
            null,
            null,
            null);
    }
}
