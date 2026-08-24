using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingThumbnailTests
{
    private static readonly DateTime Now = RecordingFactory.Now;

    [Fact]
    public void ARecordingStartsWithNoPictureYet()
        => Assert.Equal(ThumbnailState.Pending, RecordingFactory.Started().ThumbnailState);

    [Theory]
    [InlineData(ThumbnailState.Ready)]
    [InlineData(ThumbnailState.Failed)]
    [InlineData(ThumbnailState.Skipped)]
    [InlineData(ThumbnailState.Pending)]
    public void ARecordingHoldsTheFourStatesTheLedgerKnows(ThumbnailState state)
    {
        Recording recording = RecordingFactory.Started();

        recording.Illustrate(state);

        Assert.Equal(state, recording.ThumbnailState);
    }

    [Fact]
    public void AStateTheLedgerDoesNotHoldIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => RecordingFactory.Started().Illustrate((ThumbnailState)99));

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
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Truncated, 1_200_000, Now.AddHours(1));

        recording.Illustrate(ThumbnailState.Ready);

        Assert.Equal(ThumbnailState.Ready, recording.ThumbnailState);
    }
}
