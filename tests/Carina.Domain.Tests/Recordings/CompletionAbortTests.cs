using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionAbortTests
{
    [Fact]
    public void ARecordingNobodyAskedToStopIsNeverComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(asked: false);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.StoppedUnasked));
    }

    [Fact]
    public void ARecordingThisSideAskedToStopReachesComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(asked: true);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.False(verdict.Names(RecordingFault.StoppedUnasked));
    }

    [Fact]
    public void AnEndNobodyAskedForIsNotAlsoTreatedAsAWindowLeftShort()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(asked: false);

        Assert.Equal([RecordingFault.StoppedUnasked], verdict.Faults);
    }

    [Fact]
    public void AnEndNobodyAskedForDoesNotSinkARecordingPastTheWarningBand()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            written: TimeSpan.FromMilliseconds(950_000),
            asked: false);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
    }

    [Fact]
    public void AnEndNobodyAskedForDoesNotRescueAWindowThatWasNotCovered()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_250_000_000,
            written: TimeSpan.FromSeconds(900),
            asked: false);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

    [Fact]
    public void AnEmptyFileThisSideAskedToStopIsStillAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0, asked: true);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

    [Fact]
    public void WhetherTheStopWasAskedForIsAllThatIsRead()
    {
        var evidence = new RecordingEvidence(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowStart.AddSeconds(1));

        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }
}
