namespace Carina.Contracts.Tests;

public sealed class DriverEndpointsTests
{
    [Fact]
    public void PathsArePinned()
    {
        Assert.Equal("/health", DriverEndpoints.Health);
        Assert.Equal("/tuners", DriverEndpoints.Tuners);
        Assert.Equal("/sessions", DriverEndpoints.Sessions);
        Assert.Equal("/diagnostics", DriverEndpoints.Diagnostics);
        Assert.Equal("/events", DriverEndpoints.Events);
        Assert.Equal("/devices/detected", DriverEndpoints.DevicesDetected);
        Assert.Equal("/tuners/ledger", DriverEndpoints.TunerLedger);
        Assert.Equal("/restart", DriverEndpoints.Restart);
        Assert.Equal("/storage", DriverEndpoints.Storage);
    }

    [Fact]
    public void TheLedgerSitsUnderTheTunersItDescribes()
    {
        Assert.StartsWith($"{DriverEndpoints.Tuners}/", DriverEndpoints.TunerLedger);
    }

    [Fact]
    public void EveryPathIsRooted()
    {
        Assert.All(DriverEndpoints.All, path => Assert.StartsWith("/", path));
    }

    [Fact]
    public void ATunerHasAPathUnderTheLedger()
    {
        Assert.Equal("/tuners/adapter0", DriverEndpoints.Tuner("adapter0"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("../secrets")]
    [InlineData("/dev/dvb/adapter0/frontend0")]
    public void ADeviceIdOutsideTheShapeHasNoPath(string deviceId)
    {
        Assert.Throws<ArgumentException>(() => DriverEndpoints.Tuner(deviceId));
    }
}
