namespace Carina.Driver;

/// <summary>
/// Composition root of the driver process.
/// </summary>
public static class DriverHost
{
    /// <summary>
    /// Builds the driver host. The tuner, IPC and session services are added here
    /// as they are implemented; the skeleton only establishes the process shape.
    /// </summary>
    public static IHost Create(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        return builder.Build();
    }
}
