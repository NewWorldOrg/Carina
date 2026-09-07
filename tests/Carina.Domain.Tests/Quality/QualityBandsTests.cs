using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityBandsTests
{
    [Fact]
    public void EveryMeasureThisDomainNamesCarriesALevel()
        => Assert.All(QualityMetrics.All, metric => Assert.NotNull(QualityFactory.Bands().For(metric)));

    [Fact]
    public void ASetOfLevelsMissingAMeasureIsRefusedRatherThanFilledIn()
        => Assert.Throws<ArgumentException>(() => QualityBands.Of(new Dictionary<QualityMetric, ThresholdBand>
        {
            [QualityMetric.PacketsLost] = QualityFactory.PacketsLost(),
        }));

    [Fact(DisplayName = "levels that are still provisional say so")]
    public void LevelsThatAreStillProvisionalSaySo()
        => Assert.True(QualityFactory.Bands().Provisional);
}
