using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionCoverageTests
{
    [Fact]
    public void AWindowCoveredToItsLastMomentIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge();

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Findings);
        Assert.Equal(1.0, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AWindowCoveredToExactlyThePassingMarkIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(995_000));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Equal(0.995, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AMillisecondShortOfThePassingMarkIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(994_999));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.ShortOfTheWindow));
    }

    [Fact]
    public void AMillisecondPastThePassingMarkIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(995_001));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.False(verdict.Names(RecordingFinding.ShortOfTheWindow));
    }

    [Fact]
    public void AWindowCoveredToExactlyTheWarningFloorIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(950_000));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Equal(0.95, verdict.Coverage!.Value, 12);
        Assert.True(verdict.Names(RecordingFinding.ShortOfTheWindow));
    }

    [Fact]
    public void AMillisecondBelowTheWarningFloorIsAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(949_999));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.ShortOfTheWindow));
    }

    [Fact]
    public void AMillisecondAboveTheWarningFloorIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(950_001));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
    }

    [Fact]
    public void AWindowWrittenPastItsEndIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 3_000_000_000,
            written: TimeSpan.FromSeconds(1200));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Equal(1.2, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AlmostNothingWrittenIsAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 2_500, written: TimeSpan.FromMilliseconds(1));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.Equal([RecordingFinding.ShortOfTheWindow], verdict.Findings);
    }

    [Fact]
    public void AVerdictReportsThePartOfTheWindowThatWasWritten()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1_250_000_000, written: TimeSpan.FromSeconds(500));

        Assert.Equal(0.5, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AnHourLongWindowIsJudgedOnTheSameMarksAsAThousandSecondOne()
    {
        DateTime start = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var evidence = new RecordingEvidence(
            9_000_000_000,
            TimeSpan.FromMilliseconds(3_582_000),
            start,
            start.AddHours(1),
            start.AddHours(1));

        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Equal(0.995, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AnHourLongWindowAMillisecondShortOfThePassingMarkIsTruncated()
    {
        DateTime start = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);
        var evidence = new RecordingEvidence(
            9_000_000_000,
            TimeSpan.FromMilliseconds(3_581_999),
            start,
            start.AddHours(1),
            start.AddHours(1));

        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
    }
}
