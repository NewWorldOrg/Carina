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
    }

    [Fact]
    public void EveryPathIsRooted()
    {
        Assert.All(DriverEndpoints.All, path => Assert.StartsWith("/", path));
    }
}
