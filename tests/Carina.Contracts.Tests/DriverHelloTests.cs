namespace Carina.Contracts.Tests;

public sealed class DriverHelloTests
{
    private static DriverHello Hello(params string[] capabilities) =>
        new(DriverProtocol.Version, "b7f2c9", capabilities);

    [Fact]
    public void ReportedCapabilitiesAreSupported()
    {
        Assert.True(Hello(DriverCapabilities.Recording).Supports(DriverCapabilities.Recording));
    }

    [Fact]
    public void AbsentCapabilitiesAreNotSupported()
    {
        Assert.False(Hello(DriverCapabilities.Recording).Supports(DriverCapabilities.Live));
    }

    [Fact]
    public void UnknownCapabilityNamesAreCarriedWithoutComplaint()
    {
        var hello = Hello("somethingTheAppDoesNotKnow");

        Assert.False(hello.Supports(DriverCapabilities.Recording));
        Assert.Contains("somethingTheAppDoesNotKnow", hello.Capabilities);
    }

    [Fact]
    public void CapabilitiesAreNeverNull()
    {
        Assert.Empty(new DriverHello(DriverProtocol.Version, "b7f2c9", null!).Capabilities);
    }
}
