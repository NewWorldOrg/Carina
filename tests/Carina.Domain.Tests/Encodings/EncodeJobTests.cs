using Carina.Domain.Encodings;
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

    private static EncodeJob Waiting()
        => EncodeJob.Queue(
            EncodeJobId.New(),
            RecordingId.New(),
            EncodeProfileId.New(),
            EncodeDestinationId.New(),
            Queued);

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
        EncodeJob job = Running();

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
        EncodeJob job = Running();
        job.Complete(Ended);

        Assert.Throws<InvalidOperationException>(() => job.Start(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Complete(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Fail(EncodeFailure.TimedOut, "late", Ended));
        Assert.Throws<InvalidOperationException>(() => job.Cancel(Ended));
        Assert.Throws<InvalidOperationException>(() => job.Requeue(Ended));
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
            EncodeJobStatus.Queued,
            0,
            Queued,
            null,
            null,
            null));
}
