using Carina.Domain.Base;

namespace Carina.Domain.Tests;

public sealed class BroadcastDayTests
{
    [Fact]
    public void AnEveningProgrammeBelongsToTheDayTheClockAlsoCallsIt()
        => Assert.Equal(
            DayOfWeek.Tuesday,
            BroadcastDay.Of(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc)));

    [Fact]
    public void AProgrammeAfterMidnightBelongsToTheEveningItFollows()
        => Assert.Equal(
            DayOfWeek.Tuesday,
            BroadcastDay.Of(new DateTime(2026, 8, 18, 16, 30, 0, DateTimeKind.Utc)));

    [Fact]
    public void TheDayTurnsAtFourInTheMorningAndNotAtMidnight()
    {
        DateTime midnight = new(2026, 8, 18, 15, 0, 0, DateTimeKind.Utc);

        Assert.Equal(DayOfWeek.Tuesday, BroadcastDay.Of(midnight));
        Assert.Equal(DayOfWeek.Tuesday, BroadcastDay.Of(midnight.AddHours(3).AddMinutes(59)));
        Assert.Equal(DayOfWeek.Wednesday, BroadcastDay.Of(midnight.AddHours(4)));
    }

    [Fact]
    public void ATimeThatIsNotInUtcIsRefusedRatherThanReadAsThoughItWere()
        => Assert.Throws<ArgumentException>(
            () => BroadcastDay.Of(new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Local)));

    [Fact]
    public void EverySevenDaysTheWeekComesBackToWhereItWas()
    {
        DateTime began = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        for (int carried = 0; carried < 365; carried++)
        {
            Assert.Equal(BroadcastDay.Of(began.AddDays(carried)), BroadcastDay.Of(began.AddDays(carried + 7)));
        }
    }
}
