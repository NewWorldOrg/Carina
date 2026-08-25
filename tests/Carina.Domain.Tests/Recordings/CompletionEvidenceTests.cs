using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionEvidenceTests
{
    private static readonly DateTime Start = CompletionFactory.WindowStart;

    private static readonly DateTime End = CompletionFactory.WindowEnd;

    [Fact]
    public void AFileNobodyWeighedIsAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.SizeUnknown));
    }

    [Fact]
    public void AFileNobodyWeighedIsNotReportedAsAnEmptyOne()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.False(verdict.Names(RecordingFinding.NothingLanded));
    }

    [Fact]
    public void AFileNobodyWeighedFailsEvenWhenTheWindowWasCoveredAndTheStopWasAskedFor()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.Equal(1.0, verdict.Coverage!.Value, 12);
        Assert.Equal([RecordingFinding.SizeUnknown], verdict.Findings);
    }

    [Fact]
    public void ALengthNobodyCountedIsAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            new RecordingEvidence(CompletionFactory.TypicalBytes, null, Start, End, End));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.LengthUnknown));
        Assert.Null(verdict.Coverage);
    }

    [Fact]
    public void ALengthNobodyCountedIsNotReportedAsAWindowLeftShort()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            new RecordingEvidence(CompletionFactory.TypicalBytes, null, Start, End, End));

        Assert.False(verdict.Names(RecordingFinding.ShortOfTheWindow));
    }

    [Fact]
    public void EvidenceThatIsMissingEverywhereNamesEveryPieceItLacks()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(new RecordingEvidence(null, null, null, null, null));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.Equal(
            [
                RecordingFinding.WindowUnknown,
                RecordingFinding.SizeUnknown,
                RecordingFinding.LengthUnknown,
                RecordingFinding.NobodyAskedItToStop,
            ],
            verdict.Findings);
    }

    [Fact]
    public void AFileSmallerThanEmptyIsRefused()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RecordingEvidence(-1, CompletionFactory.WholeWindow, Start, End, End));

        Assert.Equal("fileSizeBytes", refusal.ParamName);
    }

    [Fact]
    public void ALengthThatRunsBackwardsIsRefused()
    {
        ArgumentOutOfRangeException refusal = Assert.Throws<ArgumentOutOfRangeException>(
            () => new RecordingEvidence(1, TimeSpan.FromMilliseconds(-1), Start, End, End));

        Assert.Equal("written", refusal.ParamName);
    }

    [Fact]
    public void AWindowThatStartsOnALocalClockIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => new RecordingEvidence(
            1,
            CompletionFactory.WholeWindow,
            DateTime.SpecifyKind(Start, DateTimeKind.Local),
            End,
            End));

        Assert.Equal("windowStart", refusal.ParamName);
    }

    [Fact]
    public void AWindowThatEndsOnALocalClockIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => new RecordingEvidence(
            1,
            CompletionFactory.WholeWindow,
            Start,
            DateTime.SpecifyKind(End, DateTimeKind.Local),
            End));

        Assert.Equal("windowEnd", refusal.ParamName);
    }

    [Fact]
    public void AStopAskedForOnALocalClockIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => new RecordingEvidence(
            1,
            CompletionFactory.WholeWindow,
            Start,
            End,
            DateTime.SpecifyKind(End, DateTimeKind.Local)));

        Assert.Equal("abortedAt", refusal.ParamName);
    }

    [Fact]
    public void AWindowOnAnUnspecifiedClockIsRefused()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => new RecordingEvidence(
            1,
            CompletionFactory.WholeWindow,
            DateTime.SpecifyKind(Start, DateTimeKind.Unspecified),
            End,
            End));

        Assert.Equal("windowStart", refusal.ParamName);
    }

    [Fact]
    public void AnEmptyFileAndAnUnweighedOneAreDifferentThings()
    {
        RecordingVerdict weighed = CompletionFactory.Judge(bytes: 0);
        RecordingVerdict unweighed = CompletionFactory.Judge(bytes: null);

        Assert.NotEqual(weighed.Findings, unweighed.Findings);
    }

    [Fact]
    public void ThereIsNoVerdictWithoutEvidence()
        => Assert.Throws<ArgumentNullException>(
            () => CompletionEvaluator.Judge(null!, CompletionFactory.Bitrate, CompletionFactory.Tolerance));

    [Fact]
    public void ThereIsNoVerdictWithoutAStreamToWeighAgainst()
        => Assert.Throws<ArgumentNullException>(
            () => CompletionEvaluator.Judge(CompletionFactory.Evidence(), null!, CompletionFactory.Tolerance));

    [Fact]
    public void ThereIsNoVerdictWithoutATolerance()
        => Assert.Throws<ArgumentNullException>(
            () => CompletionEvaluator.Judge(CompletionFactory.Evidence(), CompletionFactory.Bitrate, null!));
}
