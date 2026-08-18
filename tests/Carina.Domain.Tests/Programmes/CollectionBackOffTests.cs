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
        var visit = Visit(VisitOutcome.Complete);

        Assert.Equal(At + Settings.BetweenVisits, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void GatheringOnlyTheBasicTablesStillCountsAsGoingWell()
    {
        var visit = Visit(VisitOutcome.BasicOnly);

        Assert.Equal(At + Settings.BetweenVisits, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ComingBackShortOnceMeansWaitingTheRetryTime()
    {
        var visit = Visit(VisitOutcome.Incomplete);

        Assert.Equal(At + Settings.BeforeRetrying, CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void ComingBackShortAgainAndAgainStretchesTheWait()
    {
        var visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));

        Assert.Equal(At + (Settings.BeforeRetrying * 2), CollectionBackOff.NotBefore(visit, Settings));

        visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));

        Assert.Equal(At + (Settings.BeforeRetrying * 4), CollectionBackOff.NotBefore(visit, Settings));
    }

    [Fact]
    public void TheWaitNeverGrowsPastWhatTheSettingsAllow()
    {
        var visit = Visit(VisitOutcome.Incomplete);

        for (var again = 0; again < 30; again++)
        {
            visit.Record(VisitOutcome.Incomplete, At, TimeSpan.FromSeconds(1));
        }

        Assert.Equal(At + Settings.LongestBackOff, CollectionBackOff.NotBefore(visit, Settings));
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

    private static StreamVisit Visit(VisitOutcome outcome)
        => StreamVisit.Record(
            new NetworkId(32739),
            new TransportStreamId(32739),
            outcome,
            At,
            TimeSpan.FromSeconds(30));
}
