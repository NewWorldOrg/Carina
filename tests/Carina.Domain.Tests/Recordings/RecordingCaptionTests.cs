using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingCaptionTests
{
    private static readonly DateTime Later = RecordingFactory.Now.AddHours(2);

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
    public void AStateTheLedgerDoesNotHoldIsRefused()
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
    public void AMomentBeforeTheRecordingBeganIsRefused()
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

    private static Recording Ended()
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(RecordingFactory.Now.AddHours(1));
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_000, RecordingFactory.Now.AddHours(1));

        return recording;
    }
}
