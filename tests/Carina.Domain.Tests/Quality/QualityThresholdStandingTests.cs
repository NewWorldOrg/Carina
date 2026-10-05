using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdStandingTests
{
    private static readonly DateTime At = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a level nobody has set answers as the shipped one, and says it is provisional")]
    public void ALevelNobodyHasSetAnswersAsTheShippedOne()
    {
        IReadOnlyList<QualityThresholdStanding> standings = QualityThresholdStanding.Over([], At);

        Assert.Equal(QualityThresholdShapes.Consulted.Count, standings.Count);
        Assert.Contains(QualityThresholdKey.SupplySilence, standings.Select(standing => standing.Key));
        Assert.All(standings, standing =>
        {
            Assert.False(standing.Stored);
            Assert.True(standing.Setting.Provisional);
            Assert.Equal(0, standing.Setting.Observations);
            Assert.Equal(standing.Shape.Shipped, standing.Setting.Current);
        });
    }

    [Fact]
    public void ALevelSomebodySetAnswersAsTheStoredOneAndTheRestStillShipped()
    {
        QualityThreshold held = QualityThreshold.Rehydrate(
            QualityThresholdKey.PacketsLostWarning,
            Threshold.Of(0.0002, 0.0005, provisional: true, 0, At),
            "operator");

        IReadOnlyList<QualityThresholdStanding> standings = QualityThresholdStanding.Over([held], At);
        QualityThresholdStanding moved = standings.First(standing => standing.Key == QualityThresholdKey.PacketsLostWarning);

        Assert.True(moved.Stored);
        Assert.Equal(0.0005, moved.Setting.Current);
        Assert.Equal(0.0002, moved.Setting.Default);
        Assert.Equal("operator", moved.UpdatedBy);
        Assert.False(standings.First(standing => standing.Key == QualityThresholdKey.LockRate).Stored);
    }

    [Fact]
    public void TheShippedLevelsBuildEveryBandThisDomainJudgesAgainst()
    {
        QualityBands bands = QualityThresholdStanding.Bands(QualityThresholdStanding.Over([], At));

        Assert.All(QualityMetrics.All, metric => Assert.NotNull(bands.For(metric)));
        Assert.True(bands.Provisional);
    }

    [Fact]
    public void ALevelWithNoSecondLevelBesideItIsAlwaysInOrder()
        => Assert.True(QualityThresholdStanding.Ordered(
            QualityThresholdKey.LockRate,
            0.1,
            QualityThresholdStanding.Over([], At)));

    [Fact(DisplayName = "a warning level pushed past the unwatchable one is out of order")]
    public void AWarningLevelPushedPastTheUnwatchableOneIsOutOfOrder()
    {
        IReadOnlyList<QualityThresholdStanding> standings = QualityThresholdStanding.Over([], At);

        Assert.False(QualityThresholdStanding.Ordered(QualityThresholdKey.PacketsLostWarning, 0.5, standings));
        Assert.True(QualityThresholdStanding.Ordered(QualityThresholdKey.PacketsLostWarning, 0.0009, standings));
        Assert.False(QualityThresholdStanding.Ordered(QualityThresholdKey.PacketsLostUnwatchable, 0.00001, standings));
        Assert.True(QualityThresholdStanding.Ordered(QualityThresholdKey.PacketsLostUnwatchable, 0.002, standings));
    }

    [Fact(DisplayName = "BR-QD-023: a level says where it came from: shipped, measured or set by hand")]
    public void ALevelSaysWhereItCameFrom()
    {
        QualityThresholdMeasurement measurement = QualityThresholdMeasurement.Of(19_500, 240, 18, At.AddDays(-7), At, At);
        QualityThreshold measured = QualityThreshold.Rehydrate(
            QualityThresholdKey.CarrierToNoiseFloor,
            Threshold.Of(15_000, 19_500, provisional: false, 240, At),
            null,
            byHand: false,
            measurement);
        QualityThreshold byHand = QualityThreshold.Rehydrate(
            QualityThresholdKey.BitErrorRateCeiling,
            Threshold.Of(0.0001, 0.001, provisional: true, 0, At),
            null,
            byHand: true,
            null);

        IReadOnlyList<QualityThresholdStanding> standings = QualityThresholdStanding.Over([measured, byHand], At);

        Assert.Equal(QualityThresholdSource.Measured, Of(standings, QualityThresholdKey.CarrierToNoiseFloor).Source);
        Assert.Equal(measurement, Of(standings, QualityThresholdKey.CarrierToNoiseFloor).Measurement);
        Assert.Equal(QualityThresholdSource.ByHand, Of(standings, QualityThresholdKey.BitErrorRateCeiling).Source);
        Assert.Null(Of(standings, QualityThresholdKey.BitErrorRateCeiling).Measurement);
        Assert.Equal(QualityThresholdSource.Shipped, Of(standings, QualityThresholdKey.LockRate).Source);
    }

    private static QualityThresholdStanding Of(IReadOnlyList<QualityThresholdStanding> standings, QualityThresholdKey key)
        => standings.First(standing => standing.Key == key);
}
