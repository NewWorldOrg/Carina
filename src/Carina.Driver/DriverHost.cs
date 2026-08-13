using Carina.Driver.Configuration;
using Carina.Driver.Sessions;
using Carina.Driver.Tuning;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Driver;

public static class DriverHost
{
    public static IHost Create(string[] args, DriverConfiguration configuration)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddSingleton(configuration);
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton<ITunerDeviceFactory, TunerDeviceFactory>();
        builder.Services.AddSingleton<TunerSessionManager>();
        builder.Services.AddHostedService(provider =>
            provider.GetRequiredService<TunerSessionManager>()
        );

        return builder.Build();
    }
}
