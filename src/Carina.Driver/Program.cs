using Carina.Driver;
using Carina.Driver.Configuration;

// Read and check the configuration before anything is opened. A driver that bound
// its socket first would have told the app it was available before finding out it
// could not serve.
var configurationPath = Environment.GetEnvironmentVariable(
    DriverStartup.ConfigurationPathVariable
);

var result = DriverConfigurationReader.ReadFile(configurationPath);
var exitCode = DriverStartup.Report(result, Console.Error, configurationPath);
if (exitCode is not 0)
{
    return exitCode;
}

using var host = DriverHost.Create(args, result.Configuration!);
await host.RunAsync();

return 0;
