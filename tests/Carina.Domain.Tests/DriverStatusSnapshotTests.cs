using Carina.Contracts;
using Carina.Domain.DriverStatus;

namespace Carina.Domain.Tests;

public sealed class DriverStatusSnapshotTests
{
    private static readonly DriverObservation Connected = DriverObservation.Of(
        new DriverHello(DriverProtocol.Version, "instance-a", ["recording"]),
        []);

    [Fact]
    public void ObserveStampsTheInjectedClock()
    {
        var observedAt = new DateTimeOffset(2026, 8, 13, 3, 0, 0, TimeSpan.Zero);

        var snapshot = DriverStatusSnapshot.Observe(
            DriverObservation.NotConnected,
            new FixedTimeProvider(observedAt));

        Assert.Same(DriverObservation.NotConnected, snapshot.Observation);
        Assert.Equal(observedAt, snapshot.ObservedAt);
    }

    [Fact]
    public void ObserveRequiresATimeProvider()
    {
        Assert.Throws<ArgumentNullException>(
            () => DriverStatusSnapshot.Observe(Connected, null!));
    }

    [Fact]
    public void ObserveRequiresAnObservation()
    {
        Assert.Throws<ArgumentNullException>(
            () => DriverStatusSnapshot.Observe(null!, TimeProvider.System));
    }

    [Fact]
    public void RehydrateRestoresTheRecordedState()
    {
        var observedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        var snapshot = DriverStatusSnapshot.Rehydrate(Connected, observedAt);

        Assert.Same(Connected, snapshot.Observation);
        Assert.Equal(observedAt, snapshot.ObservedAt);
    }

    [Fact]
    public void ConstructionGoesThroughTheFactoriesOnly()
    {
        Assert.Empty(typeof(DriverStatusSnapshot).GetConstructors());
    }
}
