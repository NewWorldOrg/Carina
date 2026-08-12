namespace Carina.Contracts.Tests;

/// <summary>
/// The app decides what it may ask for by capability, not by version number: a
/// driver two releases old still answers hello, and the app has to keep working
/// with whatever subset it reports.
/// </summary>
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

    // A driver newer than the app reports names the app has never heard of. That is
    // not an error, and it must not make the answer unreadable.
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
