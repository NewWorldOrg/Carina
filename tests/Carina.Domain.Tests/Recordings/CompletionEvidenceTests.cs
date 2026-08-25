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
        Assert.True(verdict.Names(RecordingFault.SizeUnobserved));
    }

    [Fact]
    public void AFileNobodyWeighedIsNotReportedAsAnEmptyOne()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.False(verdict.Names(RecordingFault.NothingLanded));
    }

    [Fact]
    public void AFileNobodyWeighedFailsEvenWhenTheWindowWasCoveredAndTheStopWasAskedFor()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.Equal(1.0, verdict.Coverage, 12);
        Assert.Equal([RecordingFault.SizeUnobserved], verdict.Faults);
    }

    [Fact]
    public void AnEmptyFileAndAnUnweighedOneAreDifferentThings()
    {
        RecordingVerdict weighed = CompletionFactory.Judge(bytes: 0);
        RecordingVerdict unweighed = CompletionFactory.Judge(bytes: null);

        Assert.NotEqual(weighed.Faults, unweighed.Faults);
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
    public void NothingWrittenAtAllIsEvidence()
    {
        var evidence = new RecordingEvidence(1, TimeSpan.Zero, Start, End, End);

        Assert.Equal(TimeSpan.Zero, evidence.Written);
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
    public void ThereIsNoVerdictWithoutEvidence()
    {
        ArgumentNullException refusal = Assert.Throws<ArgumentNullException>(
            () => CompletionEvaluator.Judge(null!, CompletionFactory.Bitrate, CompletionFactory.Tolerance));

        Assert.Equal("evidence", refusal.ParamName);
    }

    [Fact]
    public void ThereIsNoVerdictWithoutAStreamToWeighAgainst()
    {
        ArgumentNullException refusal = Assert.Throws<ArgumentNullException>(
            () => CompletionEvaluator.Judge(CompletionFactory.Evidence(), null!, CompletionFactory.Tolerance));

        Assert.Equal("bitrate", refusal.ParamName);
    }

    [Fact]
    public void ThereIsNoVerdictWithoutATolerance()
    {
        ArgumentNullException refusal = Assert.Throws<ArgumentNullException>(
            () => CompletionEvaluator.Judge(CompletionFactory.Evidence(), CompletionFactory.Bitrate, null!));

        Assert.Equal("tolerance", refusal.ParamName);
    }
}
