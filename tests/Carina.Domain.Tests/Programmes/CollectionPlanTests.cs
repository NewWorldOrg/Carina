using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class CollectionPlanTests
{
    private static readonly DateTime Now = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Wanted = TimeSpan.FromDays(3);

    [Fact]
    public void AStreamNeverCollectedGoesBeforeOneThatIsMerelyThin()
    {
        var plan = CollectionPlan.Of(
            [Thin(2, Now.AddHours(1)), NeverVisited(1)],
            Now,
            Wanted);

        Assert.Equal([1, 2], Streams(plan));
        Assert.Equal([VisitReason.NeverCollected, VisitReason.ThinnestCoverage], plan.Select(v => v.Reason));
    }

    [Fact]
    public void AStreamCarryingAServiceNeverCollectedIsTreatedAsNeverCollected()
    {
        var plan = CollectionPlan.Of(
            [Stream(1, Now.AddHours(-1), null, Collected(1, Now.AddDays(8)), NeverCollected(2))],
            Now,
            Wanted);

        Assert.Equal(VisitReason.NeverCollected, Assert.Single(plan).Reason);
    }

    [Fact]
    public void TheThinnestServiceDecidesHowThinTheWholeStreamIs()
    {
        var stream = Stream(1, Now.AddHours(-1), null, Collected(1, Now.AddDays(8)), Collected(2, Now.AddDays(1)));

        Assert.Equal(Now.AddDays(1), CollectionPlan.ThinnestOf(stream));
    }

    [Fact]
    public void AServiceCollectedButCarryingNoProgrammesDoesNotMakeTheStreamLookThin()
    {
        var stream = Stream(1, Now.AddHours(-1), null, Collected(1, Now.AddDays(8)), Collected(2, until: null));

        Assert.Equal(Now.AddDays(8), CollectionPlan.ThinnestOf(stream));
        Assert.Equal(VisitReason.Rotation, Assert.Single(CollectionPlan.Of([stream], Now, Wanted)).Reason);
    }

    [Fact]
    public void ThinnerStreamsAreVisitedBeforeThickerOnes()
    {
        var plan = CollectionPlan.Of(
            [Thin(1, Now.AddDays(2)), Thin(2, Now.AddHours(6)), Thin(3, Now.AddDays(1))],
            Now,
            Wanted);

        Assert.Equal([2, 3, 1], Streams(plan));
    }

    [Fact]
    public void AmongStreamsMerelyTakingTheirTurnTheOneLeftLongestGoesFirst()
    {
        var plan = CollectionPlan.Of(
            [
                Stream(1, Now.AddHours(-1), null, Collected(1, Now.AddDays(8))),
                Stream(2, Now.AddDays(-2), null, Collected(1, Now.AddDays(9))),
                Stream(3, Now.AddHours(-5), null, Collected(1, Now.AddDays(10))),
            ],
            Now,
            Wanted);

        Assert.Equal([2, 3, 1], Streams(plan));
        Assert.All(plan, visit => Assert.Equal(VisitReason.Rotation, visit.Reason));
    }

    [Fact]
    public void AStreamCoveredPastWhatIsWantedIsOnlyVisitedInTurn()
    {
        var plan = CollectionPlan.Of([Thin(1, Now.AddDays(8))], Now, Wanted);

        Assert.Equal(VisitReason.Rotation, Assert.Single(plan).Reason);
    }

    [Fact]
    public void AStreamCoveredExactlyAsFarAsIsWantedIsThickEnough()
    {
        var plan = CollectionPlan.Of([Thin(1, Now + Wanted)], Now, Wanted);

        Assert.Equal(VisitReason.Rotation, Assert.Single(plan).Reason);
    }

    [Fact]
    public void AStreamStillBackingOffIsNotVisitedAtAll()
    {
        Assert.Empty(CollectionPlan.Of(
            [Stream(1, Now.AddHours(-1), Now.AddHours(1), Collected(1, Now.AddHours(1)))],
            Now,
            Wanted));
    }

    [Fact]
    public void AStreamWhoseBackingOffEndsRightNowIsVisitedAgain()
    {
        Assert.Single(CollectionPlan.Of(
            [Stream(1, Now.AddHours(-1), Now, Collected(1, Now.AddHours(1)))],
            Now,
            Wanted));
    }

    [Fact]
    public void StreamsThatAreEquallyDueAreVisitedInASettledOrder()
    {
        var plan = CollectionPlan.Of([NeverVisited(9), NeverVisited(4)], Now, Wanted);

        Assert.Equal([4, 9], Streams(plan));
    }

    [Fact]
    public void StreamsOnDifferentNetworksAreVisitedInASettledOrder()
    {
        var plan = CollectionPlan.Of(
            [
                new StreamCoverage(new NetworkId(2), new TransportStreamId(1), [], null, null),
                new StreamCoverage(new NetworkId(1), new TransportStreamId(1), [], null, null),
            ],
            Now,
            Wanted);

        Assert.Equal([1, 2], plan.Select(visit => visit.NetworkId.Value));
    }

    [Fact]
    public void APlannedVisitNamesTheStreamItMeans()
    {
        var visit = Assert.Single(CollectionPlan.Of([NeverVisited(7)], Now, Wanted));

        Assert.Equal(32739, visit.NetworkId.Value);
        Assert.Equal(7, visit.TransportStreamId.Value);
    }

    [Fact]
    public void ATimeThatIsNotInUniversalTimeIsRefused()
        => Assert.Throws<ArgumentException>(
            () => CollectionPlan.Of([], new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Local), Wanted));

    [Fact]
    public void NothingToVisitPlansNothing()
        => Assert.Empty(CollectionPlan.Of([], Now, Wanted));

    private static IEnumerable<int> Streams(IReadOnlyList<PlannedVisit> plan)
        => plan.Select(visit => visit.TransportStreamId.Value);

    private static ServiceCoverage Collected(int serviceId, DateTime? until)
        => new(new ServiceId(serviceId), until, WasEverCollected: true);

    private static ServiceCoverage NeverCollected(int serviceId)
        => new(new ServiceId(serviceId), null, WasEverCollected: false);

    private static StreamCoverage Thin(int transportStreamId, DateTime until)
        => Stream(transportStreamId, Now.AddHours(-1), null, Collected(1, until));

    private static StreamCoverage NeverVisited(int transportStreamId)
        => Stream(transportStreamId, null, null);

    private static StreamCoverage Stream(
        int transportStreamId,
        DateTime? lastCompletedAt,
        DateTime? notBefore,
        params ServiceCoverage[] services)
        => new(
            new NetworkId(32739),
            new TransportStreamId(transportStreamId),
            services,
            lastCompletedAt,
            notBefore);
}
