using System.Runtime.InteropServices;

using Carina.Driver;
using Carina.Driver.Configuration;

var configurationPath = Environment.GetEnvironmentVariable(
    DriverStartup.ConfigurationPathVariable
);

var result = DriverConfigurationReader.ReadFile(configurationPath);
if (!result.TryGetConfiguration(out var configuration, out _))
{
    return DriverStartup.Report(result, Console.Error, configurationPath);
}

var stopWasAsked = false;
using var sigterm = PosixSignalRegistration.Create(
    PosixSignal.SIGTERM,
    _ => stopWasAsked = true
);
using var sigint = PosixSignalRegistration.Create(
    PosixSignal.SIGINT,
    _ => stopWasAsked = true
);
using var sigquit = PosixSignalRegistration.Create(
    PosixSignal.SIGQUIT,
    _ => stopWasAsked = true
);

var built = DriverHost.Create(args, configuration);
if (!built.TryGetHost(out var host))
{
    return built.Refusal is DriverHostRefusal.Configuration
        ? DriverStartup.ReportUnusableConfiguration(built.Problems, Console.Error)
        : DriverStartup.ReportUnusableSocket(built.Problems, Console.Error);
}

using (host)
{
    try
    {
        await host.RunAsync();
    }
    catch (Exception error)
    {
        return DriverStartup.ReportFailure(error, Console.Error);
    }
}

return DriverStartup.ExitCodeFor(stopWasAsked);
