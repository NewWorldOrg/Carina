using Carina.Domain.Base;
using Carina.Domain.Quality;

namespace Carina.Domain.Tests.Quality;

public sealed class QualityTrendFrameTests
{
    private static readonly DateTime Now = new(2026, 9, 8, 12, 34, 56, DateTimeKind.Utc);

    [Fact(DisplayName = "a day read by the hour holds a point for each hour, the one in progress cut at now")]
    public void ADayReadByTheHourHoldsAPointForEachHour()
    {
        QualityTrendFrame frame = QualityTrendFrame.Over(1, Now, QualityTrendStep.Hour)!;

        Assert.Equal(QualityTrendStep.Hour, frame.Step);
        Assert.Equal(new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Utc), frame.Period.From);
        Assert.Equal(Now, frame.Period.Until);
        Assert.Equal(25, frame.Buckets.Count);
        Assert.Equal(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc), frame.Buckets[^1].From);
        Assert.Equal(Now, frame.Buckets[^1].Until);
    }

    [Theory(DisplayName = "a longer period widens the step rather than returning more points")]
    [InlineData(8, QualityTrendStep.Hour)]
    [InlineData(9, QualityTrendStep.ThreeHours)]
    [InlineData(40, QualityTrendStep.SixHours)]
    [InlineData(80, QualityTrendStep.TwelveHours)]
    [InlineData(150, QualityTrendStep.Day)]
    [InlineData(300, QualityTrendStep.TwoDays)]
    public void ALongerPeriodWidensTheStepRatherThanReturningMorePoints(int days, QualityTrendStep widened)
    {
        QualityTrendFrame frame = QualityTrendFrame.Over(days, Now, QualityTrendStep.Hour)!;

        Assert.Equal(widened, frame.Step);
        Assert.InRange(frame.Buckets.Count, 1, QualityTrendFrame.MostPoints);
    }

    [Fact(DisplayName = "no period that can be asked for returns more points than the cap, at any finest step")]
    public void NoPeriodThatCanBeAskedForReturnsMorePointsThanTheCap()
    {
        for (int days = 1; days <= QualityTrendFrame.MostDays; days++)
        {
            foreach (QualityTrendStep finest in QualityTrendSteps.All)
            {
                QualityTrendFrame frame = QualityTrendFrame.Over(days, Now, finest)!;

                Assert.InRange(frame.Buckets.Count, 1, QualityTrendFrame.MostPoints);
                Assert.True(frame.Step >= finest);
            }
        }
    }

    [Fact]
    public void TheWidestPeriodStillFitsTheLongestSpanAPeriodMayHave()
    {
        QualityTrendFrame frame = QualityTrendFrame.Over(QualityTrendFrame.MostDays, Now, QualityTrendStep.Hour)!;

        Assert.True(frame.Period.Span <= QualityPeriod.LongestSpan);
    }

    [Theory(DisplayName = "a day count outside the range is refused")]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    public void ADayCountOutsideTheRangeIsRefused(int days)
        => Assert.Null(QualityTrendFrame.Over(days, Now, QualityTrendStep.Hour));

    [Fact]
    public void NobodyNamingADayCountReadsTheLastDay()
        => Assert.Equal(
            QualityTrendFrame.Over(1, Now, QualityTrendStep.Hour)!.Period,
            QualityTrendFrame.Over(null, Now, QualityTrendStep.Hour)!.Period);

    [Fact]
    public void ADayStartsWhereTheBroadcastDayStarts()
    {
        QualityTrendFrame frame = QualityTrendFrame.Over(3, Now, QualityTrendStep.Day)!;

        Assert.All(
            frame.Buckets.Skip(1),
            bucket => Assert.Equal(BroadcastDay.StartsAt, JapanTimeZone.FromUtc(bucket.From).TimeOfDay));
    }

    [Fact]
    public void TheBucketsFollowOneAnotherAcrossTheWholePeriod()
    {
        QualityTrendFrame frame = QualityTrendFrame.Over(40, Now, QualityTrendStep.Hour)!;

        Assert.Equal(frame.Period.From, frame.Buckets[0].From);
        Assert.Equal(frame.Period.Until, frame.Buckets[^1].Until);
        Assert.All(
            frame.Buckets.Zip(frame.Buckets.Skip(1)),
            pair => Assert.Equal(pair.First.Until, pair.Second.From));
    }

    [Fact]
    public void AMomentIsPlacedInTheBucketHoldingItAndNowhereOutsideThePeriod()
    {
        QualityTrendFrame frame = QualityTrendFrame.Over(1, Now, QualityTrendStep.Hour)!;

        Assert.Equal(0, frame.IndexOf(frame.Period.From));
        Assert.Equal(3, frame.IndexOf(frame.Buckets[3].From.AddMinutes(59)));
        Assert.Equal(frame.Buckets.Count - 1, frame.IndexOf(Now.AddTicks(-1)));
        Assert.Equal(-1, frame.IndexOf(Now));
        Assert.Equal(-1, frame.IndexOf(frame.Period.From.AddTicks(-1)));
    }
}
