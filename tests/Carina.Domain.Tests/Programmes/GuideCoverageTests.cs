using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class GuideCoverageTests
{
    private static readonly TimeSpan EightDays = TimeSpan.FromDays(8);

    private static readonly DateTime MidnightInJapan = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Utc);

    private static readonly DateTime NoonInJapan = MidnightInJapan.AddHours(12);

    private static readonly DateTime WholeSchedule = MidnightInJapan + EightDays;

    private static readonly DateTime LateEvening = MidnightInJapan.AddHours(22);

    private static readonly DateTime NextMidnight = MidnightInJapan.AddDays(1);

    private static readonly CollectionSettings Settings = new();

    private static StreamVisit Visited(VisitOutcome outcome, DateTime attempted, DateTime? completed)
        => StreamVisit.Rehydrate(
            new NetworkId(4),
            new TransportStreamId(32_736),
            attempted,
            completed,
            outcome,
            outcome is VisitOutcome.Complete or VisitOutcome.BasicOnly ? 0 : 1,
            180_000);

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
        => Assert.Equal(WholeSchedule.AddHours(-3), GuideCoverage.WantedReachFrom(NoonInJapan, EightDays));

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
    public void AServiceWithNothingGatheredDoesNotMeetTheGoal()
        => Assert.False(GuideCoverage.IsMet(null, NoonInJapan, EightDays));

    [Fact]
    public void AStreamWhoseServicesAllReachTheGoalHasReachedIt()
        => Assert.True(GuideCoverage.IsMetByEveryServiceHoldingAGuide(
            [WholeSchedule, WholeSchedule.AddHours(-3)],
            NoonInJapan,
            EightDays));

    [Fact]
    public void AServiceHoldingNoGuideIsLeftOutOfWhatItsStreamHasToReach()
        => Assert.True(GuideCoverage.IsMetByEveryServiceHoldingAGuide(
            [WholeSchedule, null, null],
            NoonInJapan,
            EightDays));

    [Fact]
    public void OneServiceHoldingAGuideThatFallsShortKeepsItsStreamFromTheGoal()
        => Assert.False(GuideCoverage.IsMetByEveryServiceHoldingAGuide(
            [WholeSchedule, WholeSchedule.AddHours(-3).AddMinutes(-1), null],
            NoonInJapan,
            EightDays));

    [Fact]
    public void AStreamOnWhichNoServiceHoldsAGuideHasNotReachedTheGoal()
        => Assert.False(GuideCoverage.IsMetByEveryServiceHoldingAGuide([null, null], NoonInJapan, EightDays));

    [Fact]
    public void AStreamCarryingNoServiceHasNotReachedTheGoal()
        => Assert.False(GuideCoverage.IsMetByEveryServiceHoldingAGuide([], NoonInJapan, EightDays));

    [Fact]
    public void AStreamNeverVisitedIsMeasuredFromNow()
        => Assert.Equal(NoonInJapan, GuideCoverage.MeasuredFrom(null, NoonInJapan, Settings));

    [Fact]
    public void AfterMidnightAStreamVisitedOnScheduleIsStillMeasuredFromTheDayItWasHeard()
    {
        StreamVisit visit = Visited(VisitOutcome.Complete, LateEvening, LateEvening);
        DateTime measuredFrom = GuideCoverage.MeasuredFrom(visit, NextMidnight.AddMinutes(30), Settings);

        Assert.Equal(LateEvening, measuredFrom);
        Assert.True(GuideCoverage.IsMet(WholeSchedule, measuredFrom, EightDays));
    }

    [Fact]
    public void AVisitThatHeardTheGuideWithoutSettlingItStillMovesTheStartForward()
        => Assert.Equal(
            LateEvening,
            GuideCoverage.MeasuredFrom(Visited(VisitOutcome.Incomplete, LateEvening, NoonInJapan), LateEvening, Settings));

    [Fact]
    public void AVisitThatHeardNothingLeavesTheStartWhereTheGuideWasLastSettled()
        => Assert.Equal(
            NoonInJapan,
            GuideCoverage.MeasuredFrom(
                Visited(VisitOutcome.NoLock, NextMidnight.AddHours(1), NoonInJapan),
                NextMidnight.AddHours(1),
                Settings));

    [Fact]
    public void AStreamThatHasNeverBroughtTheGuideInIsMeasuredFromNow()
        => Assert.Equal(
            NextMidnight.AddHours(1),
            GuideCoverage.MeasuredFrom(
                Visited(VisitOutcome.NoBytes, NextMidnight.AddHours(1), null),
                NextMidnight.AddHours(1),
                Settings));

    [Fact]
    public void AStreamIsMeasuredFromItsLastVisitUntilTheNextIsAWholeGapOverdue()
    {
        StreamVisit visit = Visited(VisitOutcome.Complete, LateEvening, LateEvening);
        DateTime overdue = LateEvening + Settings.BetweenVisits + Settings.BetweenVisits;

        Assert.Equal(LateEvening, GuideCoverage.MeasuredFrom(visit, overdue, Settings));
        Assert.Equal(overdue.AddSeconds(1), GuideCoverage.MeasuredFrom(visit, overdue.AddSeconds(1), Settings));
    }

    [Fact]
    public void OnceVisitsStopTheGuideGatheredTheDayBeforeFallsShort()
    {
        StreamVisit visit = Visited(VisitOutcome.Complete, LateEvening, LateEvening);
        DateTime stopped = LateEvening + TimeSpan.FromDays(1);

        Assert.False(GuideCoverage.IsMet(WholeSchedule, GuideCoverage.MeasuredFrom(visit, stopped, Settings), EightDays));
    }

    [Fact]
    public void ATimeNotKeptInUtcIsRefused()
        => Assert.Throws<ArgumentException>(
            () => GuideCoverage.ScheduleStartOf(DateTime.SpecifyKind(NoonInJapan, DateTimeKind.Local)));
}
