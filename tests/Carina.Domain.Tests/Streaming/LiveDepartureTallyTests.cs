using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveDepartureTallyTests
{
    private static readonly DateTime Since = new(2026, 9, 13, 4, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Lasted = TimeSpan.FromSeconds(4);

    [Fact]
    public void ATallyAnswersForEveryWayAWireCanEnd()
    {
        LiveDepartureTally tally = new(Since, Nothing());

        Assert.Equal(Enum.GetValues<LiveDeparture>(), tally.Counted.Select(counted => counted.Departure));
    }

    [Fact]
    public void ATallyThatLeavesAWayOutIsRefused()
    {
        LiveDepartureCount[] missing = [.. Nothing().Where(counted => counted.Departure is not LiveDeparture.ServerStopping)];

        Assert.Throws<ArgumentException>(() => new LiveDepartureTally(Since, missing));
    }

    [Fact]
    public void ATallyThatNamesTheWaysInAnotherOrderIsRefused()
    {
        LiveDepartureCount[] shuffled = [.. Nothing().Reverse()];

        Assert.Throws<ArgumentException>(() => new LiveDepartureTally(Since, shuffled));
    }

    [Fact]
    public void ATallyCountingFromATimeThatIsNotUtcIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new LiveDepartureTally(DateTime.SpecifyKind(Since, DateTimeKind.Local), Nothing()));
    }

    [Fact]
    public void AWayThatHappenedSaysWhenItLastDidAndHowLongTheShortestAndTheLongestLasted()
    {
        LiveDepartureCount counted = new(
            LiveDeparture.ViewerLeft,
            3L,
            Since,
            TimeSpan.FromSeconds(3),
            TimeSpan.FromMinutes(20));

        Assert.Equal(3L, counted.Times);
        Assert.Equal(Since, counted.LastAt);
        Assert.Equal(TimeSpan.FromSeconds(3), counted.Shortest);
        Assert.Equal(TimeSpan.FromMinutes(20), counted.Longest);
    }

    [Fact]
    public void AWayCountedWithoutATimeIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new LiveDepartureCount(LiveDeparture.ViewerLeft, 3L, null, Lasted, Lasted));
    }

    [Fact]
    public void ATimeWithoutACountIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new LiveDepartureCount(LiveDeparture.ViewerLeft, 0L, Since, Lasted, Lasted));
    }

    [Fact]
    public void AWayCountedWithoutHowLongItLastedIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new LiveDepartureCount(LiveDeparture.ViewerLeft, 3L, Since));
    }

    [Fact]
    public void AWayNoWireHasEndedInThatStillNamesHowLongItLastedIsRefused()
    {
        Assert.Throws<ArgumentException>(
            () => new LiveDepartureCount(LiveDeparture.ViewerLeft, 0L, null, Lasted, Lasted));
    }

    [Fact]
    public void ALongestShorterThanTheShortestIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new LiveDepartureCount(
            LiveDeparture.ViewerLeft,
            2L,
            Since,
            TimeSpan.FromMinutes(20),
            TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void AWireThatLastedLessThanNoTimeIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveDepartureCount(
            LiveDeparture.ViewerLeft,
            1L,
            Since,
            TimeSpan.FromSeconds(-1),
            Lasted));
    }

    [Fact]
    public void AWayNothingNamesIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveDepartureCount((LiveDeparture)99, 0L, null));
    }

    [Fact]
    public void ACountBelowNoneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LiveDepartureCount(LiveDeparture.ViewerLeft, -1L, Since, Lasted, Lasted));
    }

    private static LiveDepartureCount[] Nothing()
        => [.. Enum.GetValues<LiveDeparture>().Select(departure => new LiveDepartureCount(departure, 0L, null))];
}
