using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityStandingWorstTests
{
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
