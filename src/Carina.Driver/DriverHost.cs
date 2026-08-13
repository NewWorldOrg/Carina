using Carina.Driver.Configuration;

namespace Carina.Driver;

/// <summary>
/// Composition root of the driver process.
/// </summary>
public static class DriverHost
{
    /// <summary>
    /// Builds the driver host around a configuration that has already been checked.
    /// The tuner, IPC and session services are added here as they are implemented.
    /// </summary>
    /// <remarks>
    /// The configuration arrives validated rather than being read here, so that by
    /// the time anything is constructed the decision to start has been made.
    /// </remarks>
    public static IHost Create(string[] args, DriverConfiguration configuration)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(configuration);

        return builder.Build();
    }
}
