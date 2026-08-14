using System.Runtime.InteropServices;

using Carina.Driver;
using Carina.Driver.Configuration;
using Carina.Driver.Ipc;

var configurationPath = Environment.GetEnvironmentVariable(
    DriverStartup.ConfigurationPathVariable
);

if (args is ["--shutdown-budget"])
{
    var declared = DriverConfigurationReader.ReadFile(
        configurationPath,
        checkTheFilesystem: false
    );

    if (!declared.TryGetConfiguration(out var planned, out _))
    {
        return DriverStartup.Report(declared, Console.Error, configurationPath);
    }

    Console.Out.WriteLine(DriverShutdownBudget.From(planned).TotalSeconds);
    return 0;
}

var result = DriverConfigurationReader.ReadFile(configurationPath);
if (!result.TryGetConfiguration(out var configuration, out _))
{
    return DriverStartup.Report(result, Console.Error, configurationPath);
}

if (args is ["--probe"])
{
    return await DriverProbe.RunAsync(configuration, Console.Out);
}

DriverStartup.Announce(configuration, Console.Out);

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

var built = DriverHost.Create(args, configuration, configurationPath: configurationPath);
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
