using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class CollectionBackOffTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly CollectionSettings Settings = new();

    [Fact]
    public void AVisitThatWentWellIsFollowedByTheOrdinaryWait()
    {
        StreamVisit visit = Visit(VisitOutcome.Complete);

        Assert.Equal(At + Settings.BetweenVisits, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void GatheringOnlyTheBasicTablesStillCountsAsGoingWell()
    {
        StreamVisit visit = Visit(VisitOutcome.BasicOnly);

        Assert.Equal(At + Settings.BetweenVisits, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ComingBackShortOnceMeansWaitingTheRetryTime()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        Assert.Equal(At + Settings.BeforeRetrying, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ComingBackShortAgainAndAgainStretchesTheWait()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));

        Assert.Equal(At + (Settings.BeforeRetrying * 2), CollectionBackOff.NotBefore(visit, Settings));

        visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));

        Assert.Equal(At + (Settings.BeforeRetrying * 4), CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void TheWaitNeverGrowsPastWhatTheSettingsAllow()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        for (int again = 0; again < 30; again++)
        {
            visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(At + Settings.LongestBackOff, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ComingBackShortWhileHearingPartOfTheScheduleWaitsOnlyTheRetryTime()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        for (int again = 0; again < 30; again++)
        {
            visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1), heardTheSchedule: true);
        }

        Assert.Equal(31, visit.ConsecutiveIncomplete);
        Assert.Equal(At + Settings.BeforeRetrying, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ComingBackShortHavingHeardPartOfAScheduleThatAlreadyReachesTheGoalWaitsTheOrdinaryTime()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(
            VisitOutcome.Incomplete,
            At,
            TimeSpan.FromSeconds(1),
            heardTheSchedule: true,
            reachedTheGoal: true);

        Assert.Equal(At + Settings.BetweenVisits, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ReachingTheGoalDoesNotShortenTheWaitOfAStreamThatHeardNothing()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.NoLock, At, TimeSpan.FromSeconds(1), reachedTheGoal: true);

        Assert.Equal(At + (Settings.BeforeRetrying * 2), CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void OnlyTheVisitsThatHeardNothingSinceTheScheduleWasLastHeardStretchTheWait()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        for (int again = 0; again < 10; again++)
        {
            visit.Record(VisitOutcome.NoLock, At, TimeSpan.FromSeconds(1));
        }

        visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1), heardTheSchedule: true);
        visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));

        Assert.Equal(At + Settings.BeforeRetrying, CollectionBackOff.NotBefore(visit, Settings));

        visit.Record(VisitOutcome.NoBytes, At, TimeSpan.FromSeconds(1));

        Assert.Equal(At + (Settings.BeforeRetrying * 2), CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void AVisitTheDriverCutShortIsRetriedAtOnce()
    {
        Assert.Null(CollectionBackOff.NotBefore(Visit(VisitOutcome.Interrupted), Settings));
    }

    [Theory]
    [InlineData(VisitOutcome.NoLock, true)]
    [InlineData(VisitOutcome.NoBytes, true)]
    [InlineData(VisitOutcome.Incomplete, false)]
    [InlineData(VisitOutcome.Interrupted, false)]
    [InlineData(VisitOutcome.BasicOnly, false)]
    [InlineData(VisitOutcome.Complete, false)]
    public void OnlyAFailureToTuneIsTheTunersBusiness(VisitOutcome outcome, bool reported)
        => Assert.Equal(reported, CollectionBackOff.IsWorthReportingToTheTuner(outcome));

    [Theory(DisplayName = "BR-ES-002: a visit that could not tune is a failure of the candidate, an interruption says nothing, and the rest reached it")]
    [InlineData(VisitOutcome.NoLock, TunerReport.Failed)]
    [InlineData(VisitOutcome.NoBytes, TunerReport.Failed)]
    [InlineData(VisitOutcome.Interrupted, TunerReport.Nothing)]
    [InlineData(VisitOutcome.Incomplete, TunerReport.Reached)]
    [InlineData(VisitOutcome.BasicOnly, TunerReport.Reached)]
    [InlineData(VisitOutcome.Complete, TunerReport.Reached)]
    public void WhatAVisitTellsTheCandidateItTunedWith(VisitOutcome outcome, TunerReport report)
        => Assert.Equal(report, CollectionBackOff.ReportToTheTuner(outcome));

    [Fact]
    public void AStreamNeverVisitedIsDue()
        => Assert.True(CollectionBackOff.IsDue(null, At, Settings));

    [Fact]
    public void AStreamWhoseLastVisitWasInterruptedIsDueStraightAway()
        => Assert.True(CollectionBackOff.IsDue(Visit(VisitOutcome.Interrupted), At, Settings));

    [Fact]
    public void AStreamIsNotDueWhileItsWaitLasts()
        => Assert.False(CollectionBackOff.IsDue(
            Visit(VisitOutcome.Complete),
            At + Settings.BetweenVisits - TimeSpan.FromSeconds(1),
            Settings));

    [Fact]
    public void AStreamIsDueFromTheMomentItsWaitIsOver()
        => Assert.True(CollectionBackOff.IsDue(Visit(VisitOutcome.Complete), At + Settings.BetweenVisits, Settings));

    private static StreamVisit Visit(VisitOutcome outcome)
        => StreamVisit.Record(
            new NetworkId(32739),
            new TransportStreamId(32739),
            outcome,
            At,
            TimeSpan.FromSeconds(30));
}
