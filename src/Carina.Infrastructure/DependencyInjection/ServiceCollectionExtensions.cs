using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Events;
using Carina.Domain.Scans;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Events;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Scanning;

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
            options.UseCarinaDatabase(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

        services.AddScoped<IBroadcastServiceRepository, BroadcastServiceRepository>();
        services.AddScoped<ICandidateChannelRepository, CandidateChannelRepository>();
        services.AddScoped<ISatelliteTransportStreamRepository, SatelliteTransportStreamRepository>();
        services.AddScoped<IScanRunRepository, ScanRunRepository>();
        services.AddScoped<IChannelScanOrchestrator, ChannelScanOrchestrator>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IDriverStatusReader, MonitoredDriverStatusReader>();
        services.AddSingleton<IDriverClient, DriverIpcClient>();
        services.AddSingleton<DriverConnectionMonitor>();
        services.AddSingleton<DriverSignalRelay>();
        services.AddSingleton<IDriverSignals>(provider =>
            provider.GetRequiredService<DriverSignalRelay>());
        services.TryAddSingleton<IDriverSessionResyncHook, NoopDriverSessionResyncHook>();
        services.TryAddSingleton(DriverSupervisionSettings.Default);
        services.TryAddSingleton(ScanSettings.Default);
        services.TryAddSingleton<IAppEventPublisher, NoopAppEventPublisher>();
        services.AddHostedService<DriverConnectionSupervisor>();

        return services;
    }
}
