using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityPeriodTests
{
    private static readonly DateTime Now = new(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void APeriodNobodyNamedIsTheDayBehindNow()
    {
        QualityPeriod period = QualityPeriod.Of(null, null, Now)!;

        Assert.Equal(Now, period.Until);
        Assert.Equal(Now.AddHours(-24), period.From);
    }

    [Fact]
    public void APeriodNamingOnlyAnEndReachesBackTheSameDay()
    {
        QualityPeriod period = QualityPeriod.Of(null, Now.AddHours(-1), Now)!;

        Assert.Equal(Now.AddHours(-1), period.Until);
        Assert.Equal(Now.AddHours(-25), period.From);
    }

    [Fact]
    public void APeriodWhoseEndIsNotAfterItsStartIsNoPeriod()
        => Assert.Null(QualityPeriod.Of(Now, Now, Now));

    [Fact]
    public void APeriodReachingFurtherBackThanTheLongestSpanIsRefused()
        => Assert.Null(QualityPeriod.Of(Now - QualityPeriod.LongestSpan - TimeSpan.FromDays(1), Now, Now));

    [Fact]
    public void APeriodExactlyTheLongestSpanIsAllowed()
        => Assert.NotNull(QualityPeriod.Of(Now - QualityPeriod.LongestSpan, Now, Now));

    [Fact]
    public void ATimeThatIsNotUtcIsNoTimeToAskFor()
    {
        Assert.Null(QualityPeriod.Of(new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Local), Now, Now));
        Assert.Null(QualityPeriod.Of(null, new DateTime(2026, 9, 7, 0, 0, 0, DateTimeKind.Unspecified), Now));
    }

    [Fact]
    public void APeriodHoldsItsStartAndStopsShortOfItsEnd()
    {
        QualityPeriod period = QualityPeriod.Of(Now.AddHours(-2), Now, Now)!;

        Assert.True(period.Holds(Now.AddHours(-2)));
        Assert.False(period.Holds(Now));
        Assert.Equal(TimeSpan.FromHours(2), period.Span);
    }
}
