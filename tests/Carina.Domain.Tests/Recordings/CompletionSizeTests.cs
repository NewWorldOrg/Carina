using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Recordings;

public sealed class CompletionSizeTests
{
    [Fact]
    public void AnEmptyFileIsAFailureHoweverWellTheWindowWasCovered()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.NothingLanded));
        Assert.Equal(1.0, verdict.Coverage, 12);
    }

    [Fact]
    public void AnEmptyFileIsReportedAsNothingLandingRatherThanAsALightFile()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal([RecordingFault.NothingLanded], verdict.Faults);
    }

    [Fact]
    public void AByteIsNotAnEmptyFile()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1);

        Assert.False(verdict.Names(RecordingFault.NothingLanded));
    }

    [Fact]
    public void AFileAtTheBottomOfTheWeightTheStreamAllowsIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1_800_000_000);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Faults);
    }

    [Fact]
    public void AByteBelowThatBottomIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1_799_999_999);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Equal([RecordingFault.LighterThanTheStream], verdict.Faults);
    }

    [Fact]
    public void AFileFarTooLightForWhatWasWrittenIsStillNotAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1);

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.LighterThanTheStream));
    }

    [Fact]
    public void AFileOfTheRightWeightCannotMakeUpForAWindowThatWasNotCovered()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_250_000_000,
            written: TimeSpan.FromSeconds(900));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.Equal([RecordingFault.ShortOfTheWindow], verdict.Faults);
    }

    [Fact]
    public void AFileOfTheRightWeightCannotLiftATruncatedRecording()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_400_000_000,
            written: TimeSpan.FromSeconds(960));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Equal([RecordingFault.ShortOfTheWindow], verdict.Faults);
    }

    [Fact]
    public void AFileAtTheTopOfTheWeightTheStreamAllowsIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 3_300_000_000);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Faults);
    }

    [Fact]
    public void AByteAboveThatTopIsReportedButChangesNothing()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 3_300_000_001);

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Equal([RecordingFault.HeavierThanTheStream], verdict.Faults);
    }

    [Fact]
    public void AFileHeavierThanTheStreamDoesNotLiftATruncatedRecording()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 5_000_000_000,
            written: TimeSpan.FromSeconds(960));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.HeavierThanTheStream));
        Assert.True(verdict.Names(RecordingFault.ShortOfTheWindow));
    }

    [Fact]
    public void TheWeightAllowedFollowsTheLengthWrittenRatherThanTheWindow()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 1_750_000_000,
            written: TimeSpan.FromSeconds(950));

        Assert.False(verdict.Names(RecordingFault.LighterThanTheStream));
        Assert.False(verdict.Names(RecordingFault.HeavierThanTheStream));
    }

    [Fact]
    public void TheWeightAllowedGrowsWithWhatWasWrittenPastTheWindow()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 3_500_000_000,
            written: TimeSpan.FromSeconds(1200));

        Assert.False(verdict.Names(RecordingFault.HeavierThanTheStream));
        Assert.False(verdict.Names(RecordingFault.LighterThanTheStream));
    }

    [Fact]
    public void AFileNobodyWeighedIsNeitherLightNorHeavy()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.False(verdict.Names(RecordingFault.HeavierThanTheStream));
        Assert.False(verdict.Names(RecordingFault.LighterThanTheStream));
    }

    [Fact]
    public void AFileThatLandedWhileTheClockCountedNothingIsHeavierThanTheStream()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1000, written: TimeSpan.Zero);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.Equal(
            [RecordingFault.ShortOfTheWindow, RecordingFault.HeavierThanTheStream],
            verdict.Faults);
    }

    [Fact]
    public void AnEmptyFileWithNothingWrittenIsNeitherLightNorHeavy()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0, written: TimeSpan.Zero);

        Assert.Equal(
            [RecordingFault.NothingLanded, RecordingFault.ShortOfTheWindow],
            verdict.Faults);
    }

    [Fact]
    public void TheSlackBelowTheRangeIsReadFromTheToleranceItWasHanded()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(bytes: 1_700_000_000);

        RecordingVerdict tight = CompletionFactory.JudgeBy(evidence, new CompletionTolerance(0.995, 0.95, 10));
        RecordingVerdict loose = CompletionFactory.JudgeBy(evidence, new CompletionTolerance(0.995, 0.95, 20));

        Assert.True(tight.Names(RecordingFault.LighterThanTheStream));
        Assert.False(loose.Names(RecordingFault.LighterThanTheStream));
    }

    [Fact]
    public void TheSlackAboveTheRangeIsReadFromTheToleranceItWasHanded()
    {
        RecordingEvidence evidence = CompletionFactory.Evidence(bytes: 3_500_000_000);

        RecordingVerdict tight = CompletionFactory.JudgeBy(evidence, new CompletionTolerance(0.995, 0.95, 10));
        RecordingVerdict loose = CompletionFactory.JudgeBy(evidence, new CompletionTolerance(0.995, 0.95, 20));

        Assert.True(tight.Names(RecordingFault.HeavierThanTheStream));
        Assert.False(loose.Names(RecordingFault.HeavierThanTheStream));
    }

    [Fact]
    public void AFileIsNeverBothLighterAndHeavierThanTheStream()
    {
        foreach (RecordingVerdict verdict in Weighings)
        {
            Assert.False(
                verdict.Names(RecordingFault.LighterThanTheStream)
                    && verdict.Names(RecordingFault.HeavierThanTheStream));
        }
    }

    [Fact]
    public void TheWeighingsReachBothSidesOfTheRange()
    {
        Assert.Contains(Weighings, verdict => verdict.Names(RecordingFault.LighterThanTheStream));
        Assert.Contains(Weighings, verdict => verdict.Names(RecordingFault.HeavierThanTheStream));
    }

    [Fact]
    public void ALengthNoStreamCouldHaveRunIsStillWeighedOneWayOnly()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_000_000_000_000_000_000,
            written: TimeSpan.FromDays(365 * 900));

        Assert.False(verdict.Names(RecordingFault.LighterThanTheStream));
        Assert.True(verdict.Names(RecordingFault.HeavierThanTheStream));
    }

    [Fact]
    public void AWeightThatWouldOverflowTheArithmeticIsStillCarriedOnTheRightSide()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 1,
            written: TimeSpan.FromDays(365 * 900));

        Assert.True(verdict.Names(RecordingFault.LighterThanTheStream));
        Assert.False(verdict.Names(RecordingFault.HeavierThanTheStream));
    }

    private static IReadOnlyList<RecordingVerdict> Weighings =>
    [
        CompletionFactory.Judge(),
        CompletionFactory.Judge(bytes: 1),
        CompletionFactory.Judge(bytes: 1_800_000_000),
        CompletionFactory.Judge(bytes: 3_300_000_001),
        CompletionFactory.Judge(bytes: 1000, written: TimeSpan.Zero),
        CompletionFactory.Judge(bytes: 2_000_000_000_000_000_000, written: TimeSpan.FromDays(365 * 900)),
        CompletionFactory.Judge(bytes: 1, written: TimeSpan.FromDays(365 * 900)),
        CompletionFactory.Judge(bytes: long.MaxValue, written: TimeSpan.FromDays(365 * 900)),
        CompletionFactory.Judge(bytes: long.MaxValue, written: TimeSpan.FromSeconds(1000)),
    ];
}
