using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class GuideCoverageTests
{
    private static readonly TimeSpan EightDays = TimeSpan.FromDays(8);

    private static readonly DateTime MidnightInJapan = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime NoonInJapan = MidnightInJapan.AddHours(12);

    private static readonly DateTime WholeSchedule = MidnightInJapan + EightDays;

    [Fact]
    public void TheScheduleStartsAtMidnightJapanTime()
        => Assert.Equal(MidnightInJapan, GuideCoverage.ScheduleStartOf(NoonInJapan));

    [Fact]
    public void TheLastSecondBeforeMidnightStillBelongsToTheDayThatIsEnding()
        => Assert.Equal(MidnightInJapan, GuideCoverage.ScheduleStartOf(MidnightInJapan.AddDays(1).AddSeconds(-1)));

    [Fact]
    public void MidnightStartsTheNextSchedule()
        => Assert.Equal(MidnightInJapan.AddDays(1), GuideCoverage.ScheduleStartOf(MidnightInJapan.AddDays(1)));

    [Fact]
    public void TheWantedReachIsTheWholeScheduleLessItsLastSegment()
        => Assert.Equal(WholeSchedule.AddHours(-3), GuideCoverage.WantedReachAt(NoonInJapan, EightDays));

    [Fact]
    public void AGuideReachingTheEndOfTheScheduleMeetsTheGoalEvenThoughItIsShortOfEightDaysFromNow()
    {
        Assert.True(WholeSchedule - NoonInJapan < EightDays);
        Assert.True(GuideCoverage.IsMet(WholeSchedule, NoonInJapan, EightDays));
    }

    [Fact]
    public void AGuideReachingExactlyTheWantedReachMeetsTheGoal()
        => Assert.True(GuideCoverage.IsMet(WholeSchedule.AddHours(-3), NoonInJapan, EightDays));

    [Fact]
    public void AGuideStoppingAMinuteShortOfTheWantedReachDoesNotMeetTheGoal()
        => Assert.False(GuideCoverage.IsMet(WholeSchedule.AddHours(-3).AddMinutes(-1), NoonInJapan, EightDays));

    [Fact]
    public void AWholeScheduleStillMeetsTheGoalOnTheLastSecondBeforeMidnight()
        => Assert.True(GuideCoverage.IsMet(WholeSchedule, MidnightInJapan.AddDays(1).AddSeconds(-1), EightDays));

    [Fact]
    public void AtMidnightTheSameGuideFallsShortUntilTheNewDayIsGathered()
    {
        Assert.False(GuideCoverage.IsMet(WholeSchedule, MidnightInJapan.AddDays(1), EightDays));
        Assert.True(GuideCoverage.IsMet(WholeSchedule.AddDays(1), MidnightInJapan.AddDays(1), EightDays));
    }

    [Fact]
    public void AServiceWithNothingGatheredDoesNotMeetTheGoal()
        => Assert.False(GuideCoverage.IsMet(null, NoonInJapan, EightDays));

    [Fact]
    public void ATimeNotKeptInUtcIsRefused()
        => Assert.Throws<ArgumentException>(
            () => GuideCoverage.ScheduleStartOf(DateTime.SpecifyKind(NoonInJapan, DateTimeKind.Local)));
}
