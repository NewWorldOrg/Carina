using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionWindowTests
{
    private static readonly DateTime Start = CompletionFactory.WindowStart;

    [Fact]
    public void AWindowWithNoStartIsNothingToMeasureAgainst()
    {
        RecordingVerdict verdict = Judge(null, Start.AddSeconds(1000));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.WindowUnknown));
        Assert.Null(verdict.Coverage);
    }

    [Fact]
    public void AWindowWithNoEndIsNothingToMeasureAgainst()
    {
        RecordingVerdict verdict = Judge(Start, null);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.WindowUnknown));
        Assert.Null(verdict.Coverage);
    }

    [Fact]
    public void AWindowThatEndsWhenItStartsIsNothingToMeasureAgainst()
    {
        RecordingVerdict verdict = Judge(Start, Start);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.WindowUnknown));
        Assert.Null(verdict.Coverage);
    }

    [Fact]
    public void AWindowThatEndsBeforeItStartsIsNothingToMeasureAgainst()
    {
        RecordingVerdict verdict = Judge(Start, Start.AddTicks(-1));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.WindowUnknown));
    }

    [Fact]
    public void AWindowOneTickLongIsMeasuredRatherThanRefused()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(new RecordingEvidence(
            1,
            TimeSpan.FromTicks(1),
            Start,
            Start.AddTicks(1),
            Start));

        Assert.False(verdict.Names(RecordingFinding.WindowUnknown));
        Assert.Equal(1.0, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AWindowOfOneSecondIsJudgedTheSameWayAsALongOne()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(new RecordingEvidence(
            2_500_000,
            TimeSpan.FromSeconds(1),
            Start,
            Start.AddSeconds(1),
            Start.AddSeconds(1)));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Findings);
    }

    [Fact]
    public void AWindowLeftUndeterminedIsNotReportedAsALengthNobodyCounted()
    {
        RecordingVerdict verdict = Judge(Start, null);

        Assert.False(verdict.Names(RecordingFinding.LengthUnknown));
    }

    private static RecordingVerdict Judge(DateTime? windowStart, DateTime? windowEnd)
        => CompletionFactory.Judge(new RecordingEvidence(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            windowStart,
            windowEnd,
            CompletionFactory.WindowEnd));
}
