using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityStandingsTests
{
    [Fact(DisplayName = "one subject has six answers and the three grades sit inside them")]
    public void OneSubjectHasSixAnswersAndTheThreeGradesSitInsideThem()
    {
        Assert.Equal(6, QualityStandings.All.Count);
        Assert.Equal(QualityStandings.All.Order(), Enum.GetValues<QualityStanding>().Order());
        Assert.Equal(
            [QualityStanding.Good, QualityStanding.Warning, QualityStanding.MayNotBeWatchable],
            QualityStandings.All.Where(QualityStandings.WasMeasured));
    }

    [Fact]
    public void OnlyWhatWasMeasuredCanHaveGoneBeyondALevel()
        => Assert.Equal(
            [QualityStanding.Warning, QualityStanding.MayNotBeWatchable],
            QualityStandings.All.Where(QualityStandings.WentBeyond));

    [Fact(DisplayName = "nothing counted, nothing to count, no such statistic and no supply are four answers")]
    public void NothingCountedNothingToCountNoSuchStatisticAndNoSupplyAreFourAnswers()
        => Assert.Equal(
            [QualityStanding.Unmeasured, QualityStanding.Unsupported, QualityStanding.Unreachable],
            QualityStandings.All.Where(standing => !QualityStandings.WasMeasured(standing)));

    [Fact]
    public void NothingAtAllIsUnmeasuredRatherThanGood()
        => Assert.Equal(QualityStanding.Unmeasured, QualityStandings.Worst([]));

    [Fact(DisplayName = "a reading nothing measured outranks a good one")]
    public void AReadingNothingMeasuredOutranksAGoodOne()
        => Assert.Equal(
            QualityStanding.Unmeasured,
            QualityStandings.Worst([QualityStanding.Good, QualityStanding.Unmeasured]));

    [Fact]
    public void AMeasuredBreachOutranksEveryReadingNobodyCouldTake()
        => Assert.Equal(
            QualityStanding.MayNotBeWatchable,
            QualityStandings.Worst(
            [
                QualityStanding.Unsupported,
                QualityStanding.Unreachable,
                QualityStanding.MayNotBeWatchable,
                QualityStanding.Warning,
            ]));

    [Fact(DisplayName = "what is not supported outranks what could not be reached")]
    public void WhatIsNotSupportedOutranksWhatCouldNotBeReached()
        => Assert.Equal(
            QualityStanding.Unsupported,
            QualityStandings.Worst([QualityStanding.Unmeasured, QualityStanding.Unreachable, QualityStanding.Unsupported]));

    [Fact]
    public void EveryGoodReadingTogetherIsStillGood()
        => Assert.Equal(QualityStanding.Good, QualityStandings.Worst([QualityStanding.Good, QualityStanding.Good]));

    [Fact]
    public void AStandingThisDomainDoesNotNameIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityStandings.Worst([(QualityStanding)99]));
}
