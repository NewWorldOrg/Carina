using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class StreamVisitTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Took = TimeSpan.FromSeconds(30);

    [Theory]
    [InlineData(VisitOutcome.Complete)]
    [InlineData(VisitOutcome.BasicOnly)]
    public void AVisitThatGatheredWhatItCameForCountsAsCompleted(VisitOutcome outcome)
    {
        var visit = Visit(outcome);

        Assert.Equal(At, visit.LastCompletedAt);
        Assert.Equal(0, visit.ConsecutiveIncomplete);
    }

    [Theory]
    [InlineData(VisitOutcome.Incomplete)]
    [InlineData(VisitOutcome.NoLock)]
    [InlineData(VisitOutcome.NoBytes)]
    public void AVisitThatCameBackShortIsNotACompletionAndIsCounted(VisitOutcome outcome)
    {
        var visit = Visit(outcome);

        Assert.Null(visit.LastCompletedAt);
        Assert.Equal(1, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void AVisitCutShortByTheDriverIsNotHeldAgainstTheStream()
    {
        var visit = Visit(VisitOutcome.Interrupted);

        Assert.Null(visit.LastCompletedAt);
        Assert.Equal(0, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void ComingBackShortAgainAndAgainAddsUp()
    {
        var visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took);
        visit.Record(VisitOutcome.NoLock, At.AddHours(2), Took);

        Assert.Equal(3, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void AnInterruptionLeavesWhatCameBeforeExactlyWhereItWas()
    {
        var visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took);
        visit.Record(VisitOutcome.Interrupted, At.AddHours(2), Took);

        Assert.Equal(2, visit.ConsecutiveIncomplete);

        visit.Record(VisitOutcome.Interrupted, At.AddHours(3), Took);

        Assert.Equal(2, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void AnInterruptionOnAStreamThatWasFineLeavesItFine()
    {
        var visit = Visit(VisitOutcome.Complete);

        visit.Record(VisitOutcome.Interrupted, At.AddHours(1), Took);

        Assert.Equal(0, visit.ConsecutiveIncomplete);
        Assert.Equal(At, visit.LastCompletedAt);
    }

    [Fact]
    public void GatheringItAllClearsWhatCameShortBefore()
    {
        var visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Complete, At.AddHours(1), Took);

        Assert.Equal(0, visit.ConsecutiveIncomplete);
        Assert.Equal(At.AddHours(1), visit.LastCompletedAt);
    }

    [Fact]
    public void AVisitThatCameBackShortKeepsTheLastCompletionItHad()
    {
        var visit = Visit(VisitOutcome.Complete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took);

        Assert.Equal(At, visit.LastCompletedAt);
        Assert.Equal(At.AddHours(1), visit.LastAttemptedAt);
    }

    [Fact]
    public void HowLongTheVisitTookIsKept()
        => Assert.Equal(30000, Visit(VisitOutcome.Complete).LastDurationMilliseconds);

    [Fact]
    public void ATimeThatIsNotInUniversalTimeIsRefused()
        => Assert.Throws<ArgumentException>(
            () => StreamVisit.Record(
                new NetworkId(32739),
                new TransportStreamId(32739),
                VisitOutcome.Complete,
                new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Local),
                Took));

    [Fact]
    public void AVisitThatTookNoTimeAtAllIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => StreamVisit.Record(
                new NetworkId(32739),
                new TransportStreamId(32739),
                VisitOutcome.Complete,
                At,
                TimeSpan.FromSeconds(-1)));

    private static StreamVisit Visit(VisitOutcome outcome)
        => StreamVisit.Record(new NetworkId(32739), new TransportStreamId(32739), outcome, At, Took);
}
