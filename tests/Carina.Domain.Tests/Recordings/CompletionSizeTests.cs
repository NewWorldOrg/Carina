using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionSizeTests
{
    [Fact]
    public void AnEmptyFileIsAFailureHoweverWellTheWindowWasCovered()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.NothingLanded));
        Assert.Equal(1.0, verdict.Coverage!.Value, 12);
    }

    [Fact]
    public void AnEmptyFileIsReportedAsNothingLandingRatherThanAsALightFile()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal([RecordingFinding.NothingLanded], verdict.Findings);
    }

    [Fact]
    public void AByteIsNotAnEmptyFile()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1);

        Assert.False(verdict.Names(RecordingFinding.NothingLanded));
    }

    [Fact]
    public void AFileAtTheBottomOfTheWeightTheStreamAllowsIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1_800_000_000);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Findings);
    }

    [Fact]
    public void AByteBelowThatBottomIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1_799_999_999);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Equal([RecordingFinding.LighterThanTheStream], verdict.Findings);
    }

    [Fact]
    public void AFileFarTooLightForWhatWasWrittenIsStillNotAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.LighterThanTheStream));
    }

    [Fact]
    public void AFileOfTheRightWeightCannotMakeUpForAWindowThatWasNotCovered()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_250_000_000,
            written: TimeSpan.FromSeconds(900));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.Equal([RecordingFinding.ShortOfTheWindow], verdict.Findings);
    }

    [Fact]
    public void AFileOfTheRightWeightCannotLiftATruncatedRecording()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_400_000_000,
            written: TimeSpan.FromSeconds(960));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Equal([RecordingFinding.ShortOfTheWindow], verdict.Findings);
    }

    [Fact]
    public void AFileAtTheTopOfTheWeightTheStreamAllowsIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 3_300_000_000);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Findings);
    }

    [Fact]
    public void AByteAboveThatTopIsReportedButChangesNothing()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 3_300_000_001);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Equal([RecordingFinding.HeavierThanTheStream], verdict.Findings);
    }

    [Fact]
    public void AFileHeavierThanTheStreamDoesNotLiftATruncatedRecording()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 5_000_000_000,
            written: TimeSpan.FromSeconds(960));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFinding.HeavierThanTheStream));
        Assert.True(verdict.Names(RecordingFinding.ShortOfTheWindow));
    }

    [Fact]
    public void TheWeightAllowedFollowsTheLengthWrittenRatherThanTheWindow()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 1_750_000_000,
            written: TimeSpan.FromSeconds(950));

        Assert.False(verdict.Names(RecordingFinding.LighterThanTheStream));
        Assert.False(verdict.Names(RecordingFinding.HeavierThanTheStream));
    }

    [Fact]
    public void TheWeightAllowedGrowsWithWhatWasWrittenPastTheWindow()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 3_500_000_000,
            written: TimeSpan.FromSeconds(1200));

        Assert.False(verdict.Names(RecordingFinding.HeavierThanTheStream));
        Assert.False(verdict.Names(RecordingFinding.LighterThanTheStream));
    }

    [Fact]
    public void AFileIsOnlyWeighedOnceTheLengthWrittenIsKnown()
    {
        var evidence = new RecordingEvidence(
            9_000_000_000_000,
            null,
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowEnd);

        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Assert.False(verdict.Names(RecordingFinding.HeavierThanTheStream));
        Assert.False(verdict.Names(RecordingFinding.LighterThanTheStream));
    }
}
