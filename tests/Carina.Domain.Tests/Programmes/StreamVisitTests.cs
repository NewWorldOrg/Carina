using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Tests.Programmes;

public sealed class StreamVisitTests
{
    private static readonly DateTime At = new(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Took = TimeSpan.FromSeconds(30);

    private static readonly NetworkId Network = new(32739);

    private static readonly TransportStreamId Stream = new(32739);

    [Theory]
    [InlineData(VisitOutcome.Complete)]
    [InlineData(VisitOutcome.BasicOnly)]
    public void AVisitThatGatheredWhatItCameForCountsAsCompleted(VisitOutcome outcome)
    {
        StreamVisit visit = Visit(outcome);

        Assert.Equal(At, visit.LastCompletedAt);
        Assert.Equal(0, visit.ConsecutiveIncomplete);
    }

    [Theory]
    [InlineData(VisitOutcome.Incomplete)]
    [InlineData(VisitOutcome.NoLock)]
    [InlineData(VisitOutcome.NoBytes)]
    public void AVisitThatCameBackShortIsNotACompletionAndIsCounted(VisitOutcome outcome)
    {
        StreamVisit visit = Visit(outcome);

        Assert.Null(visit.LastCompletedAt);
        Assert.Equal(1, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void AVisitCutShortByTheDriverIsNotHeldAgainstTheStream()
    {
        StreamVisit visit = Visit(VisitOutcome.Interrupted);

        Assert.Null(visit.LastCompletedAt);
        Assert.Equal(0, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void ComingBackShortAgainAndAgainAddsUp()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took);
        visit.Record(VisitOutcome.NoLock, At.AddHours(2), Took);

        Assert.Equal(3, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void AnInterruptionLeavesWhatCameBeforeExactlyWhereItWas()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took);
        visit.Record(VisitOutcome.Interrupted, At.AddHours(2), Took);

        Assert.Equal(2, visit.ConsecutiveIncomplete);

        visit.Record(VisitOutcome.Interrupted, At.AddHours(3), Took);

        Assert.Equal(2, visit.ConsecutiveIncomplete);
    }

    [Fact]
    public void AnInterruptionOnAStreamThatWasFineLeavesItFine()
    {
        StreamVisit visit = Visit(VisitOutcome.Complete);

        visit.Record(VisitOutcome.Interrupted, At.AddHours(1), Took);

        Assert.Equal(0, visit.ConsecutiveIncomplete);
        Assert.Equal(At, visit.LastCompletedAt);
    }

    [Fact]
    public void GatheringItAllClearsWhatCameShortBefore()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Complete, At.AddHours(1), Took);

        Assert.Equal(0, visit.ConsecutiveIncomplete);
        Assert.Equal(At.AddHours(1), visit.LastCompletedAt);
    }

    [Fact]
    public void AVisitThatCameBackShortKeepsTheLastCompletionItHad()
    {
        StreamVisit visit = Visit(VisitOutcome.Complete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took);

        Assert.Equal(At, visit.LastCompletedAt);
        Assert.Equal(At.AddHours(1), visit.LastAttemptedAt);
    }

    [Fact]
    public void VisitsThatHeardNoneOfTheScheduleAreCountedApart()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.NoLock, At.AddHours(1), Took);

        Assert.Equal(2, visit.ConsecutiveIncomplete);
        Assert.Equal(2, visit.ConsecutiveUnheard);
    }

    [Fact]
    public void HearingPartOfTheScheduleClearsTheUnheardCountButNotTheShortfall()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(1), Took, heardTheSchedule: true);

        Assert.Equal(2, visit.ConsecutiveIncomplete);
        Assert.Equal(0, visit.ConsecutiveUnheard);

        visit.Record(VisitOutcome.Incomplete, At.AddHours(2), Took);

        Assert.Equal(3, visit.ConsecutiveIncomplete);
        Assert.Equal(1, visit.ConsecutiveUnheard);
    }

    [Fact]
    public void AFirstVisitThatHeardPartOfTheScheduleIsShortButNotUnheard()
    {
        StreamVisit visit = StreamVisit.Record(
            new NetworkId(32739),
            new TransportStreamId(32739),
            VisitOutcome.Incomplete,
            At,
            Took,
            heardTheSchedule: true);

        Assert.Equal(1, visit.ConsecutiveIncomplete);
        Assert.Equal(0, visit.ConsecutiveUnheard);
    }

    [Fact]
    public void SettlingClearsTheUnheardCountAndAnInterruptionLeavesIt()
    {
        StreamVisit visit = Visit(VisitOutcome.NoBytes);

        visit.Record(VisitOutcome.Interrupted, At.AddHours(1), Took);

        Assert.Equal(1, visit.ConsecutiveUnheard);

        visit.Record(VisitOutcome.BasicOnly, At.AddHours(2), Took);

        Assert.Equal(0, visit.ConsecutiveUnheard);
    }

    [Fact]
    public void MoreUnheardVisitsThanShortOnesIsRefused()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => StreamVisit.Rehydrate(
                new NetworkId(32739),
                new TransportStreamId(32739),
                At,
                null,
                VisitOutcome.Incomplete,
                1,
                30000,
                2));

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

    [Fact(DisplayName = "BR-ES-002: a visit that tuned and counted part of the schedule heard it, and what it counted is the tally")]
    public void AVisitThatCountedPartOfTheScheduleHeardItAndItsCountIsTheTally()
    {
        StreamVisit visit = Visit(VisitOutcome.Incomplete);

        visit.Visited(VisitOutcome.Incomplete, At.AddHours(1), Took, [Counted(0x50)], reachedTheGoal: true);

        Assert.Equal(2, visit.ConsecutiveIncomplete);
        Assert.Equal(0, visit.ConsecutiveUnheard);
        Assert.True(visit.ReachedTheGoal);
        Assert.Equal([0x50], visit.Tally.Select(counted => counted.TableId));
    }

    [Fact(DisplayName = "BR-ES-002: a visit that tuned and counted nothing heard none of the schedule")]
    public void AVisitThatCountedNothingHeardNoneOfTheSchedule()
    {
        StreamVisit visit = StreamVisit.Visited(Network, Stream, VisitOutcome.Incomplete, At, Took, [], reachedTheGoal: false);

        visit.Visited(VisitOutcome.NoLock, At.AddHours(1), Took, [], reachedTheGoal: false);

        Assert.Equal(2, visit.ConsecutiveUnheard);
        Assert.Empty(visit.Tally);
    }

    [Fact(DisplayName = "BR-ES-002: a first visit that tuned starts its tally with what it counted")]
    public void AFirstVisitThatTunedStartsItsTallyWithWhatItCounted()
    {
        StreamVisit visit = StreamVisit.Visited(
            Network,
            Stream,
            VisitOutcome.Complete,
            At,
            Took,
            [Counted(0x50), Counted(0x58)],
            reachedTheGoal: true);

        Assert.Equal(At, visit.LastCompletedAt);
        Assert.True(visit.ReachedTheGoal);
        Assert.Equal([0x50, 0x58], visit.Tally.Select(counted => counted.TableId));
    }

    [Fact(DisplayName = "BR-ES-003: a visit every tuner turned away is interrupted and keeps the tally of the visit before")]
    public void AVisitTurnedAwayIsInterruptedAndKeepsTheTallyOfTheVisitBefore()
    {
        StreamVisit visit = StreamVisit.Visited(Network, Stream, VisitOutcome.Incomplete, At, Took, [Counted(0x50)], reachedTheGoal: true);

        visit.TurnedAway(At.AddHours(1), TimeSpan.FromSeconds(2));

        Assert.Equal(VisitOutcome.Interrupted, visit.Outcome);
        Assert.Equal(At.AddHours(1), visit.LastAttemptedAt);
        Assert.Equal(2_000, visit.LastDurationMilliseconds);
        Assert.Equal(1, visit.ConsecutiveIncomplete);
        Assert.False(visit.ReachedTheGoal);
        Assert.Equal([0x50], visit.Tally.Select(counted => counted.TableId));
    }

    [Fact(DisplayName = "BR-ES-003: a stream turned away on its first visit is written down as interrupted")]
    public void AStreamTurnedAwayOnItsFirstVisitIsInterrupted()
    {
        StreamVisit visit = StreamVisit.TurnedAway(Network, Stream, At, Took);

        Assert.Equal(VisitOutcome.Interrupted, visit.Outcome);
        Assert.Null(visit.LastCompletedAt);
        Assert.Equal(0, visit.ConsecutiveIncomplete);
        Assert.Empty(visit.Tally);
    }

    private static VisitTally Counted(int tableId)
        => VisitTally.Rehydrate(Network, Stream, new ServiceId(1024), tableId, 0x58, 8, 8, 64, 64, 0);

    private static StreamVisit Visit(VisitOutcome outcome)
        => StreamVisit.Record(new NetworkId(32739), new TransportStreamId(32739), outcome, At, Took);
}
