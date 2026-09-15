using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdShapeTests
{
    [Fact]
    public void EveryKeyThisDomainNamesHasAShape()
        => Assert.Equal(
            Enum.GetValues<QualityThresholdKey>().Order(),
            QualityThresholdShapes.All.Select(shape => shape.Key).Order());

    [Fact(DisplayName = "the levels this build offers are the ones it holds a reading against")]
    public void TheLevelsThisBuildOffersAreTheOnesItHoldsAReadingAgainst()
    {
        List<QualityThresholdKey> held = [.. QualitySignalSurvey.Keys, QualityThresholdKey.SupplySilence];

        foreach (QualityMetric metric in QualityMetrics.All)
        {
            held.Add(QualityThresholdShapes.Warning(metric));

            if (QualityThresholdShapes.Unwatchable(metric) is { } unwatchable)
            {
                held.Add(unwatchable);
            }
        }

        Assert.Equal(
            held.Distinct().Order(),
            QualityThresholdShapes.Consulted.Select(shape => shape.Key).Order());
    }

    [Fact(DisplayName = "BR-QD-003: the level a supply watch holds silence against is one the screen can move")]
    public void TheLevelASupplyWatchHoldsSilenceAgainstIsOneTheScreenCanMove()
    {
        Assert.Contains(
            QualityThresholdKey.SupplySilence,
            QualityThresholdShapes.Consulted.Select(shape => shape.Key));
        Assert.True(QualityThresholdShapes.Of(QualityThresholdKey.SupplySilence).Consulted);
    }

    [Fact]
    public void AKeyThisDomainDoesNotNameHasNoShape()
        => Assert.Throws<ArgumentOutOfRangeException>(() => QualityThresholdShapes.Of((QualityThresholdKey)99));

    [Fact]
    public void EveryShippedLevelSitsInsideItsOwnRange()
        => Assert.All(QualityThresholdShapes.All, shape => Assert.True(shape.Holds(shape.Shipped)));

    [Fact]
    public void EveryShippedLevelIsProvisionalAndStandsOnNoMeasurement()
    {
        DateTime at = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

        Assert.All(QualityThresholdShapes.All, shape =>
        {
            Threshold shipped = QualityThresholdShapes.AsShipped(shape.Key, at);

            Assert.True(shipped.Provisional);
            Assert.Equal(0, shipped.Observations);
            Assert.True(shipped.IsAsShipped);
        });
    }

    [Fact]
    public void ALevelOutsideItsRangeIsNotHeld()
    {
        QualityThresholdShape share = QualityThresholdShapes.Of(QualityThresholdKey.PacketsLostWarning);

        Assert.False(share.Holds(-0.000001));
        Assert.False(share.Holds(1.000001));
        Assert.False(share.Holds(double.NaN));
        Assert.False(share.Holds(double.PositiveInfinity));
        Assert.True(share.Holds(0));
        Assert.True(share.Holds(1));
    }

    [Fact]
    public void TheLevelsALostPacketShareIsHeldAgainstAreTheWarningAndTheUnwatchableOne()
    {
        Assert.Equal(QualityThresholdKey.PacketsLostWarning, QualityThresholdShapes.Warning(QualityMetric.PacketsLost));
        Assert.Equal(QualityThresholdKey.PacketsLostUnwatchable, QualityThresholdShapes.Unwatchable(QualityMetric.PacketsLost));
    }

    [Fact(DisplayName = "BR-QD-003: the level scrambling makes a recording unwatchable at is one the screen can move")]
    public void TheLevelsAScrambledShareIsHeldAgainstAreTheWarningAndTheUnwatchableOne()
    {
        Assert.Equal(QualityThresholdKey.PacketsLeftScrambled, QualityThresholdShapes.Warning(QualityMetric.PacketsLeftScrambled));
        Assert.Equal(
            QualityThresholdKey.PacketsLeftScrambledUnwatchable,
            QualityThresholdShapes.Unwatchable(QualityMetric.PacketsLeftScrambled));
        Assert.True(QualityThresholdShapes.Of(QualityThresholdKey.PacketsLeftScrambledUnwatchable).Consulted);
        Assert.Equal(0.0005, QualityThresholdShapes.Of(QualityThresholdKey.PacketsLeftScrambled).Shipped);
        Assert.Equal(0.01, QualityThresholdShapes.Of(QualityThresholdKey.PacketsLeftScrambledUnwatchable).Shipped);
    }

    [Fact]
    public void OverflowsCarryAWarningLevelAndNoUnwatchableOne()
        => Assert.Null(QualityThresholdShapes.Unwatchable(QualityMetric.Overflows));
}
