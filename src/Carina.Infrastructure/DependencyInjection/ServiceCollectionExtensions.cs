using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.DriverStatus;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCarinaInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<DatabaseOptions>()
            .Configure(options => options.ConnectionString =
                configuration.GetConnectionString(DatabaseOptions.ConnectionStringName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<DriverOptions>()
            .Configure(options => options.SocketPath = configuration[DriverOptions.SocketPathKey])
            .ValidateDataAnnotations()
            .Validate(
                options => string.IsNullOrEmpty(options.SocketPath)
                    || options.SocketPath.StartsWith('/'),
                $"{DriverOptions.SocketPathKey} must be an absolute path.")
            .ValidateOnStart();

        services.AddDbContext<CarinaDbContext>((provider, options) =>
            options.UseNpgsql(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDriverStatusReader, NotConnectedDriverStatusReader>();
        services.AddSingleton<IDriverClient, DriverIpcClient>();
        services.AddSingleton<DriverConnectionMonitor>();
        services.AddSingleton<DriverSignalRelay>();
        services.AddSingleton<IDriverSignals>(provider =>
            provider.GetRequiredService<DriverSignalRelay>());
        services.TryAddSingleton<IDriverSessionResyncHook, NoopDriverSessionResyncHook>();
        services.TryAddSingleton(DriverSupervisionSettings.Default);
        services.AddHostedService<DriverConnectionSupervisor>();

        return services;
    }
}
