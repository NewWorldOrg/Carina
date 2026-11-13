using Carina.Domain.Recordings;

namespace Carina.Domain.Tests.Unit.Recordings;

[Trait("Category", "Unit")]
public sealed class CompletionEvaluatorTests
{
    private static readonly DateTime Start = CompletionFactory.WindowStart;

    private static readonly DateTime End = CompletionFactory.WindowEnd;

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
        CompletionFactory.Judge(bytes: 1, written: TimeSpan.MaxValue),
        CompletionFactory.Judge(bytes: long.MaxValue, written: TimeSpan.MaxValue),
    ];

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

    [Fact]
    public void AWindowCoveredToItsLastMomentIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge();

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Faults);
        Assert.Equal(1.0, verdict.Coverage, 12);
    }

    [Fact]
    public void AWindowCoveredToExactlyThePassingMarkIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(995_000));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.Empty(verdict.Faults);
        Assert.Equal(0.995, verdict.Coverage, 12);
    }

    [Fact]
    public void AMillisecondShortOfThePassingMarkIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(994_999));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.ShortOfTheWindow));
    }

    [Fact]
    public void AMillisecondPastThePassingMarkIsComplete()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(995_001));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
        Assert.False(verdict.Names(RecordingFault.ShortOfTheWindow));
    }

    [Fact]
    public void AWindowCoveredToExactlyTheWarningFloorIsTruncated()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(950_000));

        Assert.Equal(RecordingOutcome.Truncated, verdict.Outcome);
        Assert.Equal(0.95, verdict.Coverage, 12);
        Assert.True(verdict.Names(RecordingFault.ShortOfTheWindow));
    }

    [Fact]
    public void AMillisecondBelowTheWarningFloorIsAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(written: TimeSpan.FromMilliseconds(949_999));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.ShortOfTheWindow));
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
        Assert.Equal(1.2, verdict.Coverage, 12);
    }

    [Fact]
    public void AlmostNothingWrittenIsAFailure()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 2_500, written: TimeSpan.FromMilliseconds(1));

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.Equal([RecordingFault.ShortOfTheWindow], verdict.Faults);
    }

    [Fact]
    public void AVerdictReportsThePartOfTheWindowThatWasWritten()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1_250_000_000, written: TimeSpan.FromSeconds(500));

        Assert.Equal(0.5, verdict.Coverage, 12);
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
        Assert.Empty(verdict.Faults);
        Assert.Equal(0.995, verdict.Coverage, 12);
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

    [Fact]
    public void AFileNobodyWeighedFailsARecordingThatCoveredItsWholeWindow()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: null);

        Assert.Equal(1.0, verdict.Coverage, 12);
        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

    [Fact]
    public void AnEmptyFileFailsARecordingThatCoveredItsWholeWindow()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 0);

        Assert.Equal(1.0, verdict.Coverage, 12);
        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

    [Fact]
    public void AWindowLeftFurtherShortThanTheFloorFailsAFileOfTheRightWeight()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 2_250_000_000,
            written: TimeSpan.FromSeconds(900));

        Assert.Equal([RecordingFault.ShortOfTheWindow], verdict.Faults);
        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
    }

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

    [Theory]
    [InlineData(QualityLevel.Warning)]
    [InlineData(QualityLevel.MayNotBeWatchable)]
    public void ARecordingLeftScrambledPastTheLevelIsClassifiedAsSuch(QualityLevel leftScrambled)
    {
        RecordingVerdict verdict = CompletionFactory.Judge(Scrambled(leftScrambled));

        Assert.Equal([RecordingFault.ScramblingUnresolved], verdict.Faults);
    }

    [Theory]
    [InlineData(QualityLevel.Good)]
    [InlineData(QualityLevel.Unmeasured)]
    public void ARecordingNotLeftScrambledPastTheLevelNamesNoScrambling(QualityLevel leftScrambled)
    {
        RecordingVerdict verdict = CompletionFactory.Judge(Scrambled(leftScrambled));

        Assert.Empty(verdict.Faults);
    }

    [Fact]
    public void ScramblingIsAClassificationBesideTheOutcomeRatherThanAnotherOutcome()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(Scrambled(QualityLevel.MayNotBeWatchable));

        Assert.Equal(RecordingOutcome.Complete, verdict.Outcome);
    }

    [Fact]
    public void ScramblingIsNamedBesideWhateverElseWentWrong()
    {
        RecordingEvidence evidence = new(
            CompletionFactory.TypicalBytes,
            TimeSpan.FromSeconds(900),
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowEnd,
            QualityLevel.Warning);

        RecordingVerdict verdict = CompletionFactory.Judge(evidence);

        Assert.Equal(RecordingOutcome.Failed, verdict.Outcome);
        Assert.True(verdict.Names(RecordingFault.ShortOfTheWindow));
        Assert.True(verdict.Names(RecordingFault.ScramblingUnresolved));
    }

    [Fact]
    public void EvidenceThatSaysNothingOfScramblingReadsAsUnmeasured()
    {
        Assert.Equal(QualityLevel.Unmeasured, CompletionFactory.Evidence().LeftScrambled);
    }

    private static RecordingEvidence Scrambled(QualityLevel leftScrambled)
        => new(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            CompletionFactory.WindowStart,
            CompletionFactory.WindowEnd,
            CompletionFactory.WindowEnd,
            leftScrambled);

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

    [Fact]
    public void ALengthNoClockCouldHaveRunIsStillWeighedAgainstAWeightAboveNothing()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: 1, written: TimeSpan.MaxValue);

        Assert.True(verdict.Names(RecordingFault.LighterThanTheStream));
        Assert.False(verdict.Names(RecordingFault.HeavierThanTheStream));
    }

    [Fact]
    public void TheHeaviestFileThereCanBeIsHeavyEvenForTheLongestStreamThereCanBe()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(bytes: long.MaxValue, written: TimeSpan.MaxValue);

        Assert.False(verdict.Names(RecordingFault.LighterThanTheStream));
        Assert.True(verdict.Names(RecordingFault.HeavierThanTheStream));
    }

    [Fact]
    public void AWindowThatEndsWhenItStartsIsRefusedRatherThanJudged()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Evidence(Start, Start));

        Assert.Equal("windowEnd", refusal.ParamName);
    }

    [Fact]
    public void AWindowThatEndsBeforeItStartsIsRefusedRatherThanJudged()
    {
        ArgumentException refusal = Assert.Throws<ArgumentException>(() => Evidence(Start, Start.AddTicks(-1)));

        Assert.Equal("windowEnd", refusal.ParamName);
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

        Assert.Equal(1.0, verdict.Coverage, 12);
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
        Assert.Empty(verdict.Faults);
    }

    [Fact]
    public void TheWindowIsTheDistanceBetweenItsTwoEnds()
    {
        RecordingEvidence evidence = Evidence(Start, Start.AddSeconds(1000));

        Assert.Equal(TimeSpan.FromSeconds(1000), evidence.Window);
    }

    [Fact]
    public void CoverageIsWhatWasWrittenOverThatDistance()
    {
        RecordingVerdict verdict = CompletionFactory.Judge(
            bytes: 1_250_000_000,
            written: TimeSpan.FromSeconds(500));

        Assert.Equal(0.5, verdict.Coverage, 12);
    }

    private static RecordingEvidence Evidence(DateTime windowStart, DateTime windowEnd)
        => new(
            CompletionFactory.TypicalBytes,
            CompletionFactory.WholeWindow,
            windowStart,
            windowEnd,
            CompletionFactory.WindowEnd);
}
