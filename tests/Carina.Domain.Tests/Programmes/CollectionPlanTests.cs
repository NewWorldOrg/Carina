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
            [
                Stream(2, Collected(1, Now.AddHours(1))),
                Stream(1, lastCompletedAt: null, Collected(1, Now.AddDays(8))),
            ],
            Now,
            Wanted);

        Assert.Equal([1, 2], plan.Select(visit => visit.TransportStreamId));
        Assert.Equal(VisitReason.NeverCollected, plan[0].Reason);
        Assert.Equal(VisitReason.ThinnestCoverage, plan[1].Reason);
    }

    [Fact]
    public void AStreamCarryingAServiceNeverCollectedIsTreatedAsNeverCollected()
    {
        var plan = CollectionPlan.Of(
            [Stream(1, Collected(1, Now.AddDays(8)), NeverCollected(2))],
            Now,
            Wanted);

        Assert.Equal(VisitReason.NeverCollected, Assert.Single(plan).Reason);
    }

    [Fact]
    public void TheThinnestServiceDecidesHowThinTheWholeStreamIs()
    {
        var stream = Stream(1, Collected(1, Now.AddDays(8)), Collected(2, Now.AddDays(1)));

        Assert.Equal(Now.AddDays(1), CollectionPlan.ThinnestOf(stream));
    }

    [Fact]
    public void AServiceCollectedButCarryingNoProgrammesDoesNotMakeTheStreamLookThin()
    {
        var stream = Stream(1, Collected(1, Now.AddDays(8)), Collected(2, until: null));

        Assert.Equal(Now.AddDays(8), CollectionPlan.ThinnestOf(stream));
        Assert.Equal(VisitReason.Rotation, Assert.Single(CollectionPlan.Of([stream], Now, Wanted)).Reason);
    }

    [Fact]
    public void ThinnerStreamsAreVisitedBeforeThickerOnes()
    {
        var plan = CollectionPlan.Of(
            [
                Stream(3, Collected(1, Now.AddDays(2))),
                Stream(1, Collected(1, Now.AddHours(6))),
                Stream(2, Collected(1, Now.AddDays(1))),
            ],
            Now,
            Wanted);

        Assert.Equal([1, 2, 3], plan.Select(visit => visit.TransportStreamId));
    }

    [Fact]
    public void AStreamCoveredPastWhatIsWantedIsOnlyVisitedInTurn()
    {
        var plan = CollectionPlan.Of([Stream(1, Collected(1, Now.AddDays(8)))], Now, Wanted);

        Assert.Equal(VisitReason.Rotation, Assert.Single(plan).Reason);
    }

    [Fact]
    public void AStreamStillBackingOffIsNotVisitedAtAll()
    {
        var plan = CollectionPlan.Of(
            [Stream(1, notBefore: Now.AddHours(1), services: Collected(1, Now.AddHours(1)))],
            Now,
            Wanted);

        Assert.Empty(plan);
    }

    [Fact]
    public void AStreamWhoseBackingOffHasPassedIsVisitedAgain()
    {
        var plan = CollectionPlan.Of(
            [Stream(1, notBefore: Now.AddHours(-1), services: Collected(1, Now.AddHours(1)))],
            Now,
            Wanted);

        Assert.Single(plan);
    }

    [Fact]
    public void StreamsThatAreEquallyDueAreVisitedInASettledOrder()
    {
        var plan = CollectionPlan.Of(
            [Stream(9, lastCompletedAt: null), Stream(4, lastCompletedAt: null)],
            Now,
            Wanted);

        Assert.Equal([4, 9], plan.Select(visit => visit.TransportStreamId));
    }

    [Fact]
    public void NothingToVisitPlansNothing()
    {
        Assert.Empty(CollectionPlan.Of([], Now, Wanted));
    }

    private static ServiceCoverage Collected(int serviceId, DateTime? until)
        => new(serviceId, until, WasEverCollected: true);

    private static ServiceCoverage NeverCollected(int serviceId)
        => new(serviceId, null, WasEverCollected: false);

    private static StreamCoverage Stream(int transportStreamId, params ServiceCoverage[] services)
        => Stream(transportStreamId, Now.AddHours(-1), services);

    private static StreamCoverage Stream(
        int transportStreamId,
        DateTime? lastCompletedAt,
        params ServiceCoverage[] services)
        => new(32739, transportStreamId, services, lastCompletedAt, null);

    private static StreamCoverage Stream(
        int transportStreamId,
        DateTime? notBefore,
        ServiceCoverage services)
        => new(32739, transportStreamId, [services], Now.AddHours(-1), notBefore);
}
