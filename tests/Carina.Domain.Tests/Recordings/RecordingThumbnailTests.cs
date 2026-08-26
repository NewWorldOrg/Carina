using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingThumbnailTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

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
    public void AStateTheLedgerDoesNotHoldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingFactory.Started().Illustrate((ThumbnailState)99));

    [Fact]
    public void AFaultTheLedgerDoesNotHoldIsRefused()
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

    private static Recording Settled(RecordingOutcome outcome)
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(outcome, 1_200_000, Now.AddHours(1));

        return recording;
    }
}
