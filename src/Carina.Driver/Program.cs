using Carina.Driver;

using var host = DriverHost.Create(args);
await host.RunAsync();
