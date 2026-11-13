using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityGroupQueryTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static QualityPeriod Period => QualityPeriod.Of(null, null, Now)!;

    [Fact(DisplayName = "a page size above the ceiling is cut down to it")]
    public void APageSizeAboveTheCeilingIsCutDownToIt()
        => Assert.Equal(
            QualityGroupQuery.MostPerPage,
            QualityGroupQuery.For(Period, null, null, null, QualityGroupQuery.MostPerPage + 1)!.PerPage);

    [Fact]
    public void APageSizeBelowOneFallsBackToTheOneThisDomainShips()
    {
        Assert.Equal(QualityGroupQuery.DefaultPerPage, QualityGroupQuery.For(Period, null, null, null, 0)!.PerPage);
        Assert.Equal(QualityGroupQuery.DefaultPerPage, QualityGroupQuery.For(Period, null, null, null, null)!.PerPage);
    }

    [Fact]
    public void APageBelowTheFirstIsNoPageAtAll()
        => Assert.Null(QualityGroupQuery.For(Period, null, null, 0, null));

    [Fact(DisplayName = "an ordering outside the list is refused rather than ignored")]
    public void AnOrderingOutsideTheListIsRefused()
        => Assert.Null(QualityGroupQuery.For(Period, null, (QualityGroupSort)99, null, null));

    [Fact(DisplayName = "a measure outside the list is refused rather than ignored")]
    public void AMeasureOutsideTheListIsRefused()
        => Assert.Null(QualityGroupQuery.For(Period, [(QualityMetric)99], null, null, null));

    [Fact]
    public void AskingForNoMeasureInParticularAsksForEveryOneAndReadsTheFirstAsThePrimary()
    {
        QualityGroupQuery query = QualityGroupQuery.For(Period, null, null, null, null)!;

        Assert.Equal(QualityMetrics.All, query.Metrics);
        Assert.Equal(QualityMetric.PacketsLost, query.Primary);
        Assert.Equal(QualityGroupSort.Worst, query.Sort);
        Assert.Equal(1, query.Page);
    }
}
