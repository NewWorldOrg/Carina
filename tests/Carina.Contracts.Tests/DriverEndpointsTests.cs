namespace Carina.Contracts.Tests;

/// <summary>
/// Paths are part of the contract: an old driver keeps serving the paths it was
/// built with, so renaming one here silently breaks the pairing that is the normal
/// state of a deployment. Adding a path is the only safe change.
/// </summary>
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
