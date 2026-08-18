using System.Runtime.InteropServices;

using Carina.Driver;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;

string? configurationPath = Environment.GetEnvironmentVariable(
    DriverStartup.ConfigurationPathVariable
);

if (args is ["--shutdown-budget"])
{
    DriverConfigurationResult declared = DriverConfigurationReader.ReadFile(
        configurationPath,
        checkTheFilesystem: false
    );

    if (!declared.TryGetConfiguration(out DriverConfiguration? planned, out _))
    {
        return DriverStartup.Report(declared, Console.Error, configurationPath);
    }

    Console.Out.WriteLine(DriverShutdownBudget.From(planned).TotalSeconds);
    return 0;
}

DriverConfigurationResult result = DriverConfigurationReader.ReadFile(configurationPath);
if (!result.TryGetConfiguration(out DriverConfiguration? configuration, out _))
{
    return DriverStartup.Report(result, Console.Error, configurationPath);
}

if (args is ["--probe"])
{
    return await DriverProbe.RunAsync(configuration, Console.Out);
}

DriverStartup.Announce(configuration, Console.Out);

var stopRequest = new DriverStopRequest();
using var sigterm = PosixSignalRegistration.Create(
    PosixSignal.SIGTERM,
    _ => stopRequest.Record()
);
using var sigint = PosixSignalRegistration.Create(
    PosixSignal.SIGINT,
    _ => stopRequest.Record()
);
using var sigquit = PosixSignalRegistration.Create(
    PosixSignal.SIGQUIT,
    _ => stopRequest.Record()
);

DriverHostResult built = DriverHost.Create(
    args,
    configuration,
    configurationPath: configurationPath,
    stopRequest: stopRequest
);
if (!built.TryGetHost(out IHost? host))
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

return DriverStartup.ExitCodeFor(stopRequest.WasAsked);
