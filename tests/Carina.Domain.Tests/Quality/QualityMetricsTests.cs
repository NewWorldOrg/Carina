using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityMetricsTests
{
    [Fact]
    public void AskingForNoMetricInParticularAsksForEveryOneThisDomainNames()
    {
        Assert.Equal(QualityMetrics.All, QualityMetrics.Named(null));
        Assert.Equal(QualityMetrics.All, QualityMetrics.Named([]));
    }

    [Fact]
    public void AMetricThisDomainDoesNotNameIsRefusedRatherThanIgnored()
        => Assert.Null(QualityMetrics.Named([(QualityMetric)99]));

    [Fact]
    public void TheMetricsComeBackInTheOrderThisDomainNamesThemHoweverTheyWereAsked()
        => Assert.Equal(
            [QualityMetric.PacketsLost, QualityMetric.Overflows],
            QualityMetrics.Named([QualityMetric.Overflows, QualityMetric.PacketsLost, QualityMetric.Overflows]));
}
