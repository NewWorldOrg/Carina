using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class RecordingEncodabilityTests
{
    [Fact(DisplayName = "a recording still being written is encoded once it has ended")]
    public void ARecordingStillBeingWrittenIsNotEncodedYet()
        => Assert.Equal(RecordingEncodability.StillBeingWritten, RecordingFactory.Started().Encodability);

    [Theory(DisplayName = "BR-ED2-004: a recording that ended complete or cut short has something to encode")]
    [InlineData(RecordingOutcome.Complete)]
    [InlineData(RecordingOutcome.Truncated)]
    public void ARecordingThatEndedIsEncodable(RecordingOutcome outcome)
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

        recording.Settle(outcome, 1_000_000, RecordingFactory.Now.AddHours(1));

        Assert.Equal(RecordingEncodability.Encodable, recording.Encodability);
    }

    [Fact(DisplayName = "BR-ED2-004: a recording that failed has nothing to encode")]
    public void ARecordingThatFailedHasNothingToEncode()
    {
        Recording recording = RecordingFactory.Started();
        recording.Note(RecordingFactory.Fault());
        recording.Settle(RecordingOutcome.Failed, 0, RecordingFactory.Now.AddHours(1));

        Assert.Equal(RecordingEncodability.Failed, recording.Encodability);
    }
}
