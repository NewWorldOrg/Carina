using Carina.Domain.Captions;
using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingAwaitedTests
{
    private static readonly DateTime Later = RecordingFactory.Now.AddHours(2);

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

    private static Recording Ended(RecordingOutcome outcome, bool scrambled = false)
    {
        Recording recording = RecordingFactory.Started();
        recording.Abort(RecordingFactory.Now.AddHours(1));
        recording.Note(RecordingFactory.Fault(scrambled ? RecordingFault.ScramblingUnresolved : RecordingFault.DriverLost));
        recording.Settle(outcome, outcome is RecordingOutcome.Failed ? 0 : 1_000, RecordingFactory.Now.AddHours(1));

        return recording;
    }
}
