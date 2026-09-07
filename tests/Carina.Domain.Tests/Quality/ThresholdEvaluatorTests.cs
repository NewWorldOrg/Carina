using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class ThresholdEvaluatorTests
{
    [Fact]
    public void AReadingInsideTheWarningLevelIsGood()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(0.00004, QualityFactory.PacketsLost());

        Assert.Equal(QualityStanding.Good, verdict.Standing);
        Assert.Null(verdict.Breached);
        Assert.Equal(0.00004, verdict.Observed!.Value, 12);
    }

    [Fact]
    public void AReadingExactlyAtTheWarningLevelHasReachedIt()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(0.0002, QualityFactory.PacketsLost());

        Assert.Equal(QualityStanding.Warning, verdict.Standing);
        Assert.Equal(QualityThresholdKey.PacketsLostWarning, verdict.Breached);
    }

    [Fact]
    public void AReadingExactlyAtTheUnwatchableLevelHasReachedIt()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(0.001, QualityFactory.PacketsLost());

        Assert.Equal(QualityStanding.MayNotBeWatchable, verdict.Standing);
        Assert.Equal(QualityThresholdKey.PacketsLostUnwatchable, verdict.Breached);
    }

    [Fact]
    public void AReadingBeyondEveryLevelIsStillOnlyAsBadAsTheWorstLevelNamed()
        => Assert.Equal(
            QualityStanding.MayNotBeWatchable,
            ThresholdEvaluator.Judge(0.9, QualityFactory.PacketsLost()).Standing);

    [Fact]
    public void ABandWithNoUnwatchableLevelNeverReadsAsUnwatchable()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(0.9, QualityFactory.WarningOnly());

        Assert.Equal(QualityStanding.Warning, verdict.Standing);
        Assert.Equal(QualityThresholdKey.PacketsLostWarning, verdict.Breached);
    }

    [Fact]
    public void AFloorIsReachedFromAboveRatherThanFromBelow()
    {
        ThresholdBand band = QualityFactory.LockRate(warning: 0.9, unwatchable: 0.5);

        Assert.Equal(QualityStanding.Good, ThresholdEvaluator.Judge(0.91, band).Standing);
        Assert.Equal(QualityStanding.Warning, ThresholdEvaluator.Judge(0.9, band).Standing);
        Assert.Equal(QualityStanding.Warning, ThresholdEvaluator.Judge(0.51, band).Standing);
        Assert.Equal(QualityStanding.MayNotBeWatchable, ThresholdEvaluator.Judge(0.5, band).Standing);
        Assert.Equal(QualityStanding.MayNotBeWatchable, ThresholdEvaluator.Judge(0.0, band).Standing);
    }

    [Fact(DisplayName = "BR-QD-001: nothing measured is not the same answer as nothing wrong")]
    public void NothingMeasuredIsNotTheSameAnswerAsNothingWrong()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(null, QualityFactory.PacketsLost());

        Assert.Equal(QualityStanding.Unmeasured, verdict.Standing);
        Assert.Null(verdict.Observed);
        Assert.Null(verdict.Breached);
        Assert.NotEqual(QualityStanding.Good, verdict.Standing);
    }

    [Fact]
    public void AReadingThatIsNotANumberIsNotAReadingAnythingCanBeJudgedAgainst()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ThresholdEvaluator.Judge(double.NaN, QualityFactory.PacketsLost()));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ThresholdEvaluator.Judge(double.PositiveInfinity, QualityFactory.PacketsLost()));
    }

    [Fact]
    public void AVerdictIsReachedAgainstABand()
        => Assert.Throws<ArgumentNullException>(() => ThresholdEvaluator.Judge(0.1, null!));

    [Fact(DisplayName = "BR-QV-002: a verdict keeps the level that decided it, as that level stood")]
    public void AVerdictKeepsTheLevelThatDecidedItAsThatLevelStood()
    {
        ThresholdBand band = ThresholdBand.Of(
            ThresholdSense.Ceiling,
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Moved(from: 0.0002, to: 0.0005),
            QualityThresholdKey.PacketsLostUnwatchable,
            QualityFactory.Provisional(0.001));

        ThresholdVerdict good = ThresholdEvaluator.Judge(0.0003, band);
        ThresholdVerdict warned = ThresholdEvaluator.Judge(0.0005, band);
        ThresholdVerdict unwatchable = ThresholdEvaluator.Judge(0.002, band);

        Assert.Equal(QualityStanding.Good, good.Standing);
        Assert.Equal(0.0005, good.Applied.Current, 12);
        Assert.Equal(0.0002, good.Applied.Default, 12);
        Assert.Equal(QualityThresholdKey.PacketsLostWarning, good.AppliedKey);

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, warned.AppliedKey);
        Assert.Equal(0.0005, warned.Applied.Current, 12);

        Assert.Equal(QualityThresholdKey.PacketsLostUnwatchable, unwatchable.AppliedKey);
        Assert.Equal(0.001, unwatchable.Applied.Current, 12);
    }

    [Fact(DisplayName = "BR-QV-002: a level that moves afterwards does not reach back into a verdict already reached")]
    public void ALevelThatMovesAfterwardsDoesNotReachBackIntoAVerdictAlreadyReached()
    {
        ThresholdVerdict asItStood = ThresholdEvaluator.Judge(0.0003, QualityFactory.PacketsLost(warning: 0.0002));
        ThresholdVerdict asItStandsNow = ThresholdEvaluator.Judge(0.0003, QualityFactory.PacketsLost(warning: 0.01, unwatchable: 0.02));

        Assert.Equal(QualityStanding.Warning, asItStood.Standing);
        Assert.Equal(0.0002, asItStood.Applied.Current, 12);
        Assert.Equal(QualityStanding.Good, asItStandsNow.Standing);
        Assert.Equal(0.0002, asItStood.Applied.Current, 12);
    }

    [Fact(DisplayName = "BR-QV-002: an incident carries the level that decided it rather than the one in force later")]
    public void AnIncidentCarriesTheLevelThatDecidedItRatherThanTheOneInForceLater()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(0.0003, QualityFactory.PacketsLost(warning: 0.0002));

        QualityIncident incident = QualityIncident.Detect(
            QualityIncidentId.New(),
            QualityFactory.Settled,
            verdict.Breached!.Value,
            QualitySubject.Of(QualitySubjectKind.Channel, "32736/1024"),
            verdict.Observed!.Value,
            verdict.Applied);

        ThresholdBand loosened = QualityFactory.PacketsLost(warning: 0.05, unwatchable: 0.1);

        Assert.Equal(0.0002, incident.Applied.Current, 12);
        Assert.Equal(0.05, loosened.Warning.Current, 12);
    }

    [Fact(DisplayName = "BR-QD-003: a verdict says the level that decided it is provisional and how little stands behind it")]
    public void AVerdictSaysTheLevelThatDecidedItIsProvisionalAndHowLittleStandsBehindIt()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(0.0003, QualityFactory.PacketsLost());

        Assert.True(verdict.Provisional);
        Assert.Equal(0, verdict.Applied.Observations);
    }

    [Fact]
    public void AVerdictIsProvisionalExactlyWhenTheLevelThatDecidedItIs()
    {
        ThresholdBand band = ThresholdBand.Of(
            ThresholdSense.Ceiling,
            QualityThresholdKey.PacketsLostWarning,
            QualityFactory.Firm(0.0002),
            QualityThresholdKey.PacketsLostUnwatchable,
            QualityFactory.Provisional(0.001));

        Assert.False(ThresholdEvaluator.Judge(0.0003, band).Provisional);
        Assert.True(ThresholdEvaluator.Judge(0.002, band).Provisional);
    }

    [Fact]
    public void NothingMeasuredIsStillHeldAgainstTheLevelStandingAtTheTime()
    {
        ThresholdVerdict verdict = ThresholdEvaluator.Judge(null, QualityFactory.PacketsLost(warning: 0.0002));

        Assert.Equal(QualityThresholdKey.PacketsLostWarning, verdict.AppliedKey);
        Assert.Equal(0.0002, verdict.Applied.Current, 12);
        Assert.True(verdict.Provisional);
    }
}
