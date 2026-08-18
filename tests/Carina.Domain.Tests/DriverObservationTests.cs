using Carina.Contracts;
using Carina.Domain.DriverStatus;

namespace Carina.Domain.Tests;

public sealed class DriverObservationTests
{
    private static DriverHello Hello(
        int protocolVersion = DriverProtocol.Version,
        bool draining = false,
        string[]? capabilities = null)
        => new(protocolVersion, "instance-a", capabilities ?? []) { Draining = draining };

    [Fact]
    public void StartsNotConnectedWithNothingKnown()
    {
        DriverObservation observation = DriverObservation.NotConnected;

        Assert.Equal(DriverConnection.NotConnected, observation.Connection);
        Assert.Null(observation.Hello);
        Assert.Empty(observation.MissingCapabilities);
        Assert.False(observation.DriverUpdateRequired);
    }

    [Fact]
    public void ASeenHelloMeansConnected()
    {
        var observation = DriverObservation.Of(Hello(capabilities: ["recording"]), []);

        Assert.Equal(DriverConnection.Connected, observation.Connection);
        Assert.NotNull(observation.Hello);
        Assert.False(observation.DriverUpdateRequired);
    }

    [Fact]
    public void ADrainingHelloMeansDraining()
    {
        var observation = DriverObservation.Of(Hello(draining: true), []);

        Assert.Equal(DriverConnection.Draining, observation.Connection);
    }

    [Fact]
    public void ADrainingSignalFlipsTheConnectionAndKeepsTheHello()
    {
        DriverObservation observation = DriverObservation.Of(Hello(capabilities: ["recording"]), []).WhileDraining();

        Assert.Equal(DriverConnection.Draining, observation.Connection);
        Assert.NotNull(observation.Hello);
    }

    [Fact]
    public void AMissingCapabilityCallsForADriverUpdate()
    {
        var observation = DriverObservation.Of(Hello(capabilities: ["recording"]), ["live"]);

        Assert.True(observation.DriverUpdateRequired);
        Assert.Equal(["live"], observation.MissingCapabilities);
    }

    [Fact]
    public void AnOlderProtocolCallsForADriverUpdate()
    {
        var observation = DriverObservation.Of(Hello(protocolVersion: DriverProtocol.Version - 1), []);

        Assert.True(observation.DriverUpdateRequired);
    }

    [Fact]
    public void ANewerProtocolIsToleratedBecauseCapabilitiesDecide()
    {
        var observation = DriverObservation.Of(Hello(protocolVersion: DriverProtocol.Version + 1), []);

        Assert.False(observation.DriverUpdateRequired);
    }
}
