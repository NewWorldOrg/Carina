using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityObservationTests
{
    [Fact]
    public void AnObservationOfSomethingMeasuredCarriesWhatWasRead()
    {
        QualityObservation observation = QualityFactory.Measured(0.00004);

        Assert.Equal(QualityStanding.Good, observation.Standing);
        Assert.True(observation.WasMeasured);
        Assert.Equal(0.00004, observation.Observed!.Value, 12);
    }

    [Fact(DisplayName = "an observation nothing counted carries no figure to average")]
    public void AnObservationNothingCountedCarriesNoFigureToAverage()
    {
        QualityObservation observation = QualityFactory.Unmeasured();

        Assert.Equal(QualityStanding.Unmeasured, observation.Standing);
        Assert.False(observation.WasMeasured);
        Assert.Null(observation.Observed);
    }

    [Fact]
    public void AnObservationOfSomethingUnsupportedOrUnreachableCarriesNoFigureEither()
    {
        Assert.Null(QualityFactory.Unsupported().Observed);
        Assert.Equal(QualityStanding.Unsupported, QualityFactory.Unsupported().Standing);
        Assert.Null(QualityFactory.Unreachable().Observed);
        Assert.Equal(QualityStanding.Unreachable, QualityFactory.Unreachable().Standing);
    }

    [Fact]
    public void AnObservationIsTakenOfSomething()
    {
        Assert.Throws<ArgumentNullException>(() => QualityObservation.Of(null!, ThresholdEvaluator.Judge(0.1, QualityFactory.PacketsLost())));
        Assert.Throws<ArgumentNullException>(() => QualityObservation.Of(QualityFactory.Facet(), null!));
        Assert.Throws<ArgumentNullException>(() => QualityObservation.Unsupported(null!));
        Assert.Throws<ArgumentNullException>(() => QualityObservation.Unreachable(null!));
    }
}
