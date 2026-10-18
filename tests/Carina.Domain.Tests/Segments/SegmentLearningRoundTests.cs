using Carina.Domain.Segments;

namespace Carina.Domain.Tests.Segments;

public sealed class SegmentLearningRoundTests
{
    [Theory(DisplayName = "BR-CD-004: a day of learning runs from three in the morning in Japan to three the next morning")]
    [InlineData("2026-10-18T18:00:00Z", "2026-10-19")]
    [InlineData("2026-10-18T17:59:59Z", "2026-10-18")]
    [InlineData("2026-10-19T14:59:59Z", "2026-10-19")]
    [InlineData("2026-10-19T15:00:00Z", "2026-10-19")]
    [InlineData("2026-10-19T17:59:59Z", "2026-10-19")]
    public void ADayRunsFromThreeInTheMorningInJapan(string at, string day)
    {
        DateTime moment = DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);

        Assert.Equal(DateOnly.Parse(day, System.Globalization.CultureInfo.InvariantCulture), SegmentLearningRound.DayOf(moment));
        Assert.Equal(SegmentLearningRound.DayOf(moment), SegmentLearningRound.Taken(moment).Day);
    }

    [Fact(DisplayName = "a round is taken in universal time, no earlier than the day it is for")]
    public void ARoundIsTakenNoEarlierThanItsDay()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 18), SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 18), at).Day);
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 21), at));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Taken(DateTime.SpecifyKind(at, DateTimeKind.Local)));
    }
}
