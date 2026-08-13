using Carina.Driver.Configuration;

namespace Carina.Driver;

public static class DriverHost
{
    public static IHost Create(string[] args, DriverConfiguration configuration)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(configuration);

        return builder.Build();
    }
}
