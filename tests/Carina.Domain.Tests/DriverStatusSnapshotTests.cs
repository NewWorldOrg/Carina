using Carina.Domain.DriverStatus;

namespace Carina.Domain.Tests;

public sealed class DriverStatusSnapshotTests
{
    [Fact]
    public void ObserveStampsTheInjectedClock()
    {
        var observedAt = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero);

        var snapshot = DriverStatusSnapshot.Observe(DriverConnection.NotConnected, new FixedTimeProvider(observedAt));

        Assert.Equal(DriverConnection.NotConnected, snapshot.Connection);
        Assert.Equal(observedAt, snapshot.ObservedAt);
    }

    [Fact]
    public void ObserveRequiresATimeProvider()
    {
        Assert.Throws<ArgumentNullException>(() => DriverStatusSnapshot.Observe(DriverConnection.Connected, null!));
    }

    [Fact]
    public void RehydrateRestoresTheRecordedState()
    {
        var observedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var snapshot = DriverStatusSnapshot.Rehydrate(DriverConnection.Connected, observedAt);

        Assert.Equal(DriverConnection.Connected, snapshot.Connection);
        Assert.Equal(observedAt, snapshot.ObservedAt);
    }

    [Fact]
    public void ConstructionGoesThroughTheFactoriesOnly()
    {
        Assert.Empty(typeof(DriverStatusSnapshot).GetConstructors());
    }
}
