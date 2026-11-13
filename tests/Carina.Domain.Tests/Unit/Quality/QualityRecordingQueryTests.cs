using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Unit.Quality;

[Trait("Category", "Unit")]
public sealed class QualityRecordingQueryTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    private static QualityPeriod Period => QualityPeriod.Of(null, null, Now)!;

    [Fact(DisplayName = "a page size above the ceiling is cut down to it")]
    public void APageSizeAboveTheCeilingIsCutDownToIt()
        => Assert.Equal(
            QualityRecordingQuery.MostPerPage,
            QualityRecordingQuery.For(Period, null, null, null, QualityRecordingQuery.MostPerPage + 1)!.PerPage);

    [Fact]
    public void APageBelowTheFirstIsNoPageAtAll()
        => Assert.Null(QualityRecordingQuery.For(Period, null, null, 0, null));

    [Fact(DisplayName = "an ordering outside the list is refused rather than ignored")]
    public void AnOrderingOutsideTheListIsRefused()
        => Assert.Null(QualityRecordingQuery.For(Period, null, (QualityRecordingSort)99, null, null));

    [Fact(DisplayName = "a measure outside the list is refused rather than ignored")]
    public void AMeasureOutsideTheListIsRefused()
        => Assert.Null(QualityRecordingQuery.For(Period, [(QualityMetric)99], null, null, null));

    [Fact]
    public void AskingForOneMeasureMakesThatOneThePrimary()
        => Assert.Equal(
            QualityMetric.Overflows,
            QualityRecordingQuery.For(Period, [QualityMetric.Overflows], null, null, null)!.Primary);
}
