using Carina.Domain.Streaming;

namespace Carina.Domain.Tests.Streaming;

public sealed class LiveDepartureTallyTests
{
    private static readonly DateTime Since = new(2026, 9, 13, 4, 0, 0, DateTimeKind.Utc);

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
    public void AWayThatHappenedSaysWhenItLastDid()
    {
        LiveDepartureCount counted = new(LiveDeparture.ViewerLeft, 3L, Since);

        Assert.Equal(3L, counted.Times);
        Assert.Equal(Since, counted.LastAt);
    }

    [Fact]
    public void AWayCountedWithoutATimeIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new LiveDepartureCount(LiveDeparture.ViewerLeft, 3L, null));
    }

    [Fact]
    public void ATimeWithoutACountIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new LiveDepartureCount(LiveDeparture.ViewerLeft, 0L, Since));
    }

    [Fact]
    public void AWayNothingNamesIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveDepartureCount((LiveDeparture)99, 0L, null));
    }

    [Fact]
    public void ACountBelowNoneIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new LiveDepartureCount(LiveDeparture.ViewerLeft, -1L, Since));
    }

    private static LiveDepartureCount[] Nothing()
        => [.. Enum.GetValues<LiveDeparture>().Select(departure => new LiveDepartureCount(departure, 0L, null))];
}
