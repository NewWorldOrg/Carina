namespace Carina.Domain.Tests;

public sealed class JapanTimeZoneTests
{
    [Fact]
    public void NowConvertsUtcToJapanStandardTime()
    {
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero));

        var now = JapanTimeZone.Now(clock);

        Assert.Equal(new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.FromHours(9)), now);
        Assert.Equal(TimeSpan.FromHours(9), now.Offset);
    }

    [Fact]
    public void TodayRollsOverAtJapanMidnight()
    {
        var beforeMidnight = new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 14, 59, 59, TimeSpan.Zero));
        var afterMidnight = new FixedTimeProvider(new DateTimeOffset(2026, 8, 13, 15, 0, 0, TimeSpan.Zero));

        Assert.Equal(new DateOnly(2026, 8, 13), JapanTimeZone.Today(beforeMidnight));
        Assert.Equal(new DateOnly(2026, 8, 14), JapanTimeZone.Today(afterMidnight));
    }

    [Fact]
    public void FromUtcTreatsUnspecifiedKindAsUtc()
    {
        var stored = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

        Assert.Equal(new DateTime(2026, 1, 1, 9, 0, 0), JapanTimeZone.FromUtc(stored));
    }

    [Fact]
    public void NowRequiresATimeProvider()
    {
        Assert.Throws<ArgumentNullException>(() => JapanTimeZone.Now(null!));
    }
}
