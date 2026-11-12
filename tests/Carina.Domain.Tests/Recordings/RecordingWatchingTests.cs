using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingWatchingTests
{
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
}
