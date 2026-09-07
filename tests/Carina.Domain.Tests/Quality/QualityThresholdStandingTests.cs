using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityThresholdStandingTests
{
    private static readonly DateTime At = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "a level nobody has set answers as the shipped one, and says it is provisional")]
    public void ALevelNobodyHasSetAnswersAsTheShippedOne()
    {
        IReadOnlyList<QualityThresholdStanding> standings = QualityThresholdStanding.Over([], At);

        Assert.Equal(QualityThresholdShapes.All.Count, standings.Count);
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
}
