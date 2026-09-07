using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdShapeTests
{
    [Fact]
    public void EveryKeyThisDomainNamesHasAShape()
        => Assert.Equal(
            Enum.GetValues<QualityThresholdKey>().Order(),
            QualityThresholdShapes.All.Select(shape => shape.Key).Order());

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

    [Fact]
    public void TheOtherMeasuresCarryAWarningLevelAndNoUnwatchableOne()
    {
        Assert.Null(QualityThresholdShapes.Unwatchable(QualityMetric.PacketsLeftScrambled));
        Assert.Null(QualityThresholdShapes.Unwatchable(QualityMetric.Overflows));
    }
}
