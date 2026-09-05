using Carina.Domain.Encodings;
using Carina.Domain.Machines;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Encodings;

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

    [Fact(DisplayName = "BR-ES-002: a job stands at one of five places and there is no sixth")]
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

    [Fact(DisplayName = "BR-ES-002: what the library is shown is a fifth answer for a recording no job ever touched")]
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

    [Fact(DisplayName = "BR-ES-002: a job that was called off leaves the recording unencoded, not failed")]
    public void AJobThatWasCalledOffLeavesTheRecordingUnencodedRatherThanFailed()
    {
        Assert.Equal(EncodeStanding.NotEncoded, EncodeStandings.Of(EncodeJobStatus.Cancelled));
        Assert.Equal(EncodeStanding.Failed, EncodeStandings.Of(EncodeJobStatus.Failed));
    }

    [Fact(DisplayName = "BR-ES-001: a job begins waiting, on its first attempt, and has ended nothing")]
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

    [Fact(DisplayName = "BR-ES-001: waiting becomes running, and running becomes finished")]
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

    [Fact(DisplayName = "BR-ES-001: a job that has ended cannot be moved again")]
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

    [Fact(DisplayName = "BR-ED2-009: a job keeps the output root it was queued into, so the destination may move without it")]
    public void AJobKeepsTheOutputRootItWasQueuedInto()
    {
        EncodeJob job = Waiting();

        Assert.Equal(Primary, job.OutputRoot);
        Assert.Null(job.ArtefactName);
    }

    [Fact(DisplayName = "BR-ED2-009: the work file is named for the job and the attempt it is on")]
    public void TheWorkFileIsNamedForTheJobAndTheAttemptItIsOn()
    {
        EncodeJob job = Running();

        Assert.Equal(EncodeFileName.Working(job.RecordingId, job.Id, 1), job.WorkFileName);

        job.Requeue(Ended);

        Assert.Equal(EncodeFileName.Working(job.RecordingId, job.Id, 2), job.WorkFileName);
    }

    [Fact(DisplayName = "BR-ED2-009: the artefact is named while the job runs, and only then")]
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

    [Fact(DisplayName = "BR-ED2-009: a job cannot finish without having said what it made")]
    public void AJobCannotFinishWithoutHavingSaidWhatItMade()
    {
        EncodeJob job = Running();

        Assert.Throws<InvalidOperationException>(() => job.Complete(Ended));
        Assert.Equal(EncodeJobStatus.Running, job.Status);
    }

    [Fact(DisplayName = "BR-ED2-009: naming the artefact again by the same name is its own success seen again, not a second claim")]
    public void NamingTheArtefactAgainByTheSameNameIsNotASecondClaim()
    {
        EncodeJob job = Named();
        EncodeFileName same = EncodeFileName.Artefact(job.RecordingId, job.ProfileId);

        job.Name(same);

        Assert.Equal(same, job.ArtefactName);
    }

    [Fact(DisplayName = "BR-ED2-009: a job that has named its artefact cannot be talked into another name")]
    public void AJobThatHasNamedItsArtefactCannotBeTalkedIntoAnotherName()
    {
        EncodeJob job = Named();

        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Artefact(job.RecordingId, EncodeProfileId.New())));
    }

    [Fact(DisplayName = "BR-ED2-009: the name survives a requeue, so the next attempt can recognise its own success")]
    public void TheNameSurvivesARequeue()
    {
        EncodeJob job = Named();
        EncodeFileName named = job.ArtefactName!;

        job.Requeue(Ended);

        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Equal(named, job.ArtefactName);
    }

    [Fact(DisplayName = "BR-ED2-009: the artefact is named for the recording and the profile the job was queued with")]
    public void TheArtefactIsNamedForTheRecordingAndTheProfileTheJobWasQueuedWith()
    {
        EncodeJob job = Running();

        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Artefact(RecordingId.New(), job.ProfileId)));
        Assert.Throws<InvalidOperationException>(() => job.Name(EncodeFileName.Working(job.RecordingId, job.Id, 1)));
    }

    [Fact(DisplayName = "BR-ES-001: a job that is still waiting cannot finish without having run")]
    public void AJobThatIsStillWaitingCannotFinishWithoutHavingRun()
    {
        EncodeJob job = Waiting();

        Assert.Throws<InvalidOperationException>(() => job.Complete(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Fail(EncodeFailure.SourceMissing, "gone", Ended));
        Assert.Throws<InvalidOperationException>(() => job.Requeue(Ended));
    }

    [Fact(DisplayName = "BR-ES-001: a job picked up again is put back to waiting with its attempt counted")]
    public void AJobPickedUpAgainIsPutBackToWaitingWithItsAttemptCounted()
    {
        EncodeJob job = Running();

        job.Requeue(Ended);

        Assert.Equal(EncodeJobStatus.Queued, job.Status);
        Assert.Equal(EncodeJob.FirstAttempt + 1, job.Attempt);
        Assert.Equal(Ended, job.QueuedAt);
        Assert.Null(job.StartedAt);
    }

    [Fact(DisplayName = "BR-ED2-011: a job found running when the process comes up goes back to the queue with its attempt counted")]
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

    [Fact(DisplayName = "BR-ED2-011: a job on its last attempt when the process comes up is given up as timed out, not put back")]
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
            null);

        EncodeRecovery recovery = job.Recover(3, Ended);

        Assert.Equal(EncodeRecovery.GivenUp, recovery);
        Assert.Equal(EncodeJobStatus.Failed, job.Status);
        Assert.Equal(3, job.Attempt);
        Assert.Equal(EncodeFailure.TimedOut, job.Failure!.Failure);
        Assert.Contains("attempt 3 of the 3", job.Failure.Note, StringComparison.Ordinal);
        Assert.Equal(Ended, job.EndedAt);
    }

    [Fact(DisplayName = "BR-ED2-011: only a job the ledger holds as running is picked up again, and it gets at least one attempt")]
    public void OnlyARunningJobIsPickedUpAgain()
    {
        Assert.Throws<InvalidOperationException>(() => Waiting().Recover(3, Ended));
        Assert.Throws<ArgumentOutOfRangeException>(() => Running().Recover(0, Ended));
    }

    [Fact(DisplayName = "BR-ED2-005: a claim carries a job only when the ledger holds it as running")]
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

    [Fact(DisplayName = "BR-ED2-012: a failure is a classification, and the words beside it are not the reason")]
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

    [Fact(DisplayName = "BR-ED2-012: the six reasons a job fails for are these and no other")]
    public void TheSixReasonsAJobFailsForAreTheseAndNoOther()
    {
        Assert.Equal(
            [
                EncodeFailure.FfmpegExitedNonZero,
                EncodeFailure.NotEnoughRoom,
                EncodeFailure.SourceMissing,
                EncodeFailure.CapabilityUnavailable,
                EncodeFailure.TimedOut,
                EncodeFailure.DestinationCollision,
            ],
            Enum.GetValues<EncodeFailure>());
    }

    [Fact(DisplayName = "BR-ED2-012: a reason nobody named cannot be recorded as one")]
    public void AReasonNobodyNamedCannotBeRecordedAsOne()
    {
        EncodeJob job = Running();

        Assert.Throws<ArgumentOutOfRangeException>(() => job.Fail((EncodeFailure)99, "who knows", Ended));
    }

    [Fact(DisplayName = "BR-ED2-012: only the tail of what the programme said is kept")]
    public void OnlyTheTailOfWhatTheProgrammeSaidIsKept()
    {
        string said = new string('a', EncodeNote.Longest) + "the part that matters";

        Assert.Equal(EncodeNote.Longest, EncodeNote.Of(said).Length);
        Assert.EndsWith("the part that matters", EncodeNote.Of(said), StringComparison.Ordinal);
    }

    [Fact(DisplayName = "BR-ED2-012: calling a job off is a different move from failing it")]
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
            null));
}

public sealed class EncodeJobRunMarkTests
{
    private static readonly DateTime Queued = new(2026, 9, 5, 3, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime Started = new(2026, 9, 5, 3, 0, 5, DateTimeKind.Utc);

    private static readonly DateTime Later = new(2026, 9, 5, 3, 10, 0, DateTimeKind.Utc);

    private static readonly TimeSpan TenMinutes = TimeSpan.FromMinutes(10);

    private static readonly OutputRoot Primary = new("primary");

    private static readonly RunningProgramme Ffmpeg = new(4242, Started.AddSeconds(1));

    private static readonly EncodeRoute Degraded = new(EncodeEncoder.Vaapi, EncodeEncoder.Software, EncodeSwerve.TheCardIsOutOfReach);

    private static EncodeJob Waiting()
        => EncodeJob.Queue(EncodeJobId.New(), RecordingId.New(), EncodeProfileId.New(), EncodeDestinationId.New(), Primary, Queued);

    private static EncodeJob Running()
    {
        EncodeJob job = Waiting();
        job.Start(Started);

        return job;
    }

    private static EncodeJob Marked()
    {
        EncodeJob job = Running();
        job.Routed(Degraded);
        job.Spawned(Ffmpeg);
        job.Reached(EncodeProgress.Of(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), 2, false), Later);

        return job;
    }

    [Fact(DisplayName = "BR-EV-004: where a run went is written on the job, so a degraded run is in the ledger and not only in a log")]
    public void WhereARunWentIsWrittenOnTheJob()
    {
        EncodeJob job = Running();

        job.Routed(Degraded);

        Assert.Equal(EncodeEncoder.Vaapi, job.Route!.Asked);
        Assert.Equal(EncodeEncoder.Software, job.Route.Ran);
        Assert.Equal(EncodeSwerve.TheCardIsOutOfReach, job.Route.Swerved);
        Assert.True(job.Route.WasDegraded);
    }

    [Fact(DisplayName = "BR-ED2-011: the programme a run started is written on the job as its id and when it began, together")]
    public void TheProgrammeARunStartedIsWrittenOnTheJob()
    {
        EncodeJob job = Running();

        job.Spawned(Ffmpeg);

        Assert.Equal(4242, job.Programme!.ProcessId);
        Assert.Equal(Started.AddSeconds(1), job.Programme.StartedAt);
    }

    [Fact(DisplayName = "BR-ED2-014: headway is written on the job as the portion done, what is left and when that was reported")]
    public void HeadwayIsWrittenOnTheJobWithWhenItWasReported()
    {
        EncodeJob job = Running();

        job.Reached(EncodeProgress.Of(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), 2, false), Later);

        Assert.Equal(0.5, job.Headway!.Portion);
        Assert.Equal(TimeSpan.FromSeconds(15), job.Headway.Left);
        Assert.Equal(Later, job.Headway.At);
    }

    [Fact(DisplayName = "BR-ED2-014: a running job is quiet for as long as it has been since its last headway, and before any headway since it started")]
    public void ARunningJobIsQuietForAsLongAsSinceItsLastHeadway()
    {
        EncodeJob fresh = Running();
        EncodeJob heard = Marked();

        Assert.Equal(Later - Started, fresh.QuietFor(Later));
        Assert.Equal(TimeSpan.Zero, heard.QuietFor(Later));
        Assert.Equal(TimeSpan.FromMinutes(5), heard.QuietFor(Later.AddMinutes(5)));
        Assert.Null(Waiting().QuietFor(Later));
    }

    [Fact(DisplayName = "BR-ED2-014: a running job that has made no headway for as long as a run may go quiet is stalled, and the ledger's 'running' is not to be read as such")]
    public void ARunningJobWithNoHeadwayForTooLongIsStalled()
    {
        EncodeJob job = Marked();

        Assert.False(job.IsStalled(Later.AddMinutes(9), TenMinutes));
        Assert.True(job.IsStalled(Later.AddMinutes(10), TenMinutes));
        Assert.True(job.IsStalled(Later.AddHours(3), TenMinutes));
    }

    [Fact(DisplayName = "BR-ED2-014: a job that has yet to report anything is stalled from its start, not never")]
    public void AJobThatHasReportedNothingIsStalledFromItsStart()
    {
        EncodeJob job = Running();

        Assert.False(job.IsStalled(Started.AddMinutes(9), TenMinutes));
        Assert.True(job.IsStalled(Started.AddMinutes(10), TenMinutes));
    }

    [Fact(DisplayName = "BR-ED2-014: only a running job can be stalled; a job that ended quiet is not, and a job cannot be asked about a quiet of no length")]
    public void OnlyARunningJobCanBeStalled()
    {
        EncodeJob ended = Marked();
        ended.Fail(EncodeFailure.TimedOut, "quiet", Later.AddHours(1));

        Assert.False(ended.IsStalled(Later.AddHours(3), TenMinutes));
        Assert.False(Waiting().IsStalled(Later.AddHours(3), TenMinutes));
        Assert.Throws<ArgumentOutOfRangeException>(() => Running().IsStalled(Later, TimeSpan.Zero));
    }

    [Fact(DisplayName = "BR-ED2-011: a job put back in the queue carries nothing of the run it was on: no route, no programme, no headway")]
    public void AJobPutBackCarriesNothingOfTheRunItWasOn()
    {
        EncodeJob job = Marked();

        job.Requeue(Later);

        Assert.Null(job.Route);
        Assert.Null(job.Programme);
        Assert.Null(job.Headway);
    }

    [Fact(DisplayName = "BR-ED2-011: a job that ends keeps where it ran and how far it got, and lets go of the programme, which has exited")]
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

    [Fact(DisplayName = "BR-ES-001: only a running job runs somewhere, has a programme, or makes headway")]
    public void OnlyARunningJobIsMarked()
    {
        EncodeJob waiting = Waiting();
        EncodeProgress progress = EncodeProgress.Of(TimeSpan.Zero, null, 0, false);

        Assert.Throws<InvalidOperationException>(() => waiting.Routed(Degraded));
        Assert.Throws<InvalidOperationException>(() => waiting.Spawned(Ffmpeg));
        Assert.Throws<InvalidOperationException>(() => waiting.Reached(progress, Later));
    }

    [Fact(DisplayName = "BR-ED2-011: a row that says a waiting job has a programme, or ran somewhere, is not one the ledger can hold")]
    public void ARowThatSaysAWaitingJobHasAProgrammeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Queued, null, programme: Ffmpeg));
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Queued, null, route: Degraded));
        Assert.Throws<ArgumentException>(() => Rehydrated(EncodeJobStatus.Completed, Later, programme: Ffmpeg));
        Assert.NotNull(Rehydrated(EncodeJobStatus.Running, null, route: Degraded, programme: Ffmpeg).Programme);
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
            Queued,
            status is EncodeJobStatus.Queued ? null : Started,
            ended,
            null,
            status is EncodeJobStatus.Completed ? EncodeFileName.Artefact(recording, profile) : null,
            route,
            programme,
            null);
    }
}
