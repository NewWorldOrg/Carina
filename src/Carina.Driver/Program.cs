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

using var host = DriverHost.Create(args, configuration);

var lifetime = (IHostApplicationLifetime)
    host.Services.GetService(typeof(IHostApplicationLifetime))!;
var stopWasAsked = false;
lifetime.ApplicationStopping.Register(() => stopWasAsked = true);

await host.RunAsync();

return DriverStartup.ExitCodeFor(stopWasAsked);
