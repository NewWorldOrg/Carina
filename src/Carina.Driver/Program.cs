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

using var host = DriverHost.Create(args, configuration);
await host.RunAsync();

return DriverStartup.ExitCodeFor(stopWasAsked);
