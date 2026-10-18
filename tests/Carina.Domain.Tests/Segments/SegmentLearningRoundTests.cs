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

    [Theory(DisplayName = "BR-CD-004: a round learns while no round has kept a candidate, and otherwise only when something changed after the last one that did was taken")]
    [InlineData(null, null, true)]
    [InlineData(null, "2026-10-19T03:00:00Z", true)]
    [InlineData("2026-10-19T03:00:00Z", null, false)]
    [InlineData("2026-10-19T03:00:00Z", "2026-10-19T03:00:00Z", false)]
    [InlineData("2026-10-19T03:00:00Z", "2026-10-19T02:59:59Z", false)]
    [InlineData("2026-10-19T03:00:00Z", "2026-10-19T03:00:01Z", true)]
    public void ARoundLearnsOnlyFromSomethingNew(string? lastLearnedFrom, string? lastChanged, bool news)
    {
        Assert.Equal(news, SegmentLearningRound.HasNews(Moment(lastLearnedFrom), Moment(lastChanged)));
    }

    [Fact(DisplayName = "a round is taken in universal time, no earlier than the day it is for")]
    public void ARoundIsTakenNoEarlierThanItsDay()
    {
        DateTime at = new(2026, 10, 19, 18, 30, 0, DateTimeKind.Utc);

        Assert.Equal(new DateOnly(2026, 10, 18), SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 18), at, null).Day);
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 21), at, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => SegmentLearningRound.Rehydrate(new DateOnly(2026, 10, 18), at, 0));
        Assert.Throws<ArgumentException>(() => SegmentLearningRound.Taken(DateTime.SpecifyKind(at, DateTimeKind.Local)));
    }

    private static DateTime? Moment(string? at)
        => at is null ? null : DateTime.Parse(at, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
}
