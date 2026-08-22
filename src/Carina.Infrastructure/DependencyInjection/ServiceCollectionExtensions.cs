using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Events;
using Carina.Domain.Programmes;
using Carina.Domain.Scans;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Collection;
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

        services.AddSingleton<IValidateOptions<CollectionOptions>, CollectionValidation>();
        services.AddOptions<CollectionOptions>()
            .Configure(options => options.ReadFrom(configuration))
            .ValidateOnStart();

        services.AddDbContext<CarinaDbContext>((provider, options) =>
            options.UseCarinaDatabase(provider.GetRequiredService<IOptions<DatabaseOptions>>().Value.ConnectionString));

        services.AddScoped<IAtomicWrite, DatabaseAtomicWrite>();
        services.AddScoped<IAuthSessionRepository, AuthSessionRepository>();
        services.AddScoped<ILocalAccountRepository, LocalAccountRepository>();
        services.AddScoped<IOidcSettingsRepository, OidcSettingsRepository>();
        services.AddScoped<IOidcDirectory, OidcDirectory>();
        services.AddScoped<IBroadcastServiceRepository, BroadcastServiceRepository>();
        services.AddScoped<IProgrammeRepository, ProgrammeRepository>();
        services.AddScoped<IProgrammeSearchRepository, ProgrammeSearchRepository>();
        services.AddScoped<IStreamVisitRepository, StreamVisitRepository>();
        services.AddScoped<ICollectionEpochRepository, CollectionEpochRepository>();
        services.AddScoped<IArchivedProgrammeRepository, ArchivedProgrammeRepository>();
        services.AddScoped<ICandidateChannelRepository, CandidateChannelRepository>();
        services.AddScoped<ISatelliteTransportStreamRepository, SatelliteTransportStreamRepository>();
        services.AddScoped<IScanRunRepository, ScanRunRepository>();
        services.AddScoped<IChannelScanOrchestrator, ChannelScanOrchestrator>();
        services.AddScoped<ScanApplier>();
        services.AddScoped<IBroadcastStreamDirectory, BroadcastStreamDirectory>();
        services.AddScoped<ITuneFailureReporter, CandidateTuneFailureReporter>();
        services.AddScoped<ProgrammeWriter>();
        services.AddScoped<StreamVisitor>();
        services.AddScoped<CollectionRound>();
        services.AddScoped<ArchiveTransfer>();

        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<IOidcGateway, OidcGateway>();
        services.TryAddSingleton(SessionPolicy.Default);
        services.TryAddSingleton(PasswordHashPolicy.Default);
        services.TryAddSingleton(LoginRatePolicy.Default);
        services.TryAddSingleton(OidcLoginPolicy.Default);
        services.TryAddSingleton<OidcDirectoryCache>();
        services.TryAddSingleton<IOidcReachability, OidcReachability>();
        services.TryAddSingleton<IPendingOidcLoginStore, PendingOidcLoginStore>();
        services.TryAddSingleton<IPasswordHasher, Argon2idPasswordHasher>();
        services.TryAddSingleton<ILoginThrottle, LoginThrottle>();
        services.AddSingleton<IDriverStatusReader, MonitoredDriverStatusReader>();
        services.AddSingleton<IDriverClient, DriverIpcClient>();
        services.AddSingleton<DriverConnectionMonitor>();
        services.AddSingleton<ScanRunner>();
        services.AddSingleton<DriverSignalRelay>();
        services.AddSingleton<IDriverSignals>(provider =>
            provider.GetRequiredService<DriverSignalRelay>());
        services.TryAddSingleton<IDriverSessionResyncHook, NoopDriverSessionResyncHook>();
        services.TryAddSingleton(DriverSupervisionSettings.Default);
        services.TryAddSingleton(ScanSettings.Default);
        services.TryAddSingleton<CollectionSettings>(provider =>
            provider.GetRequiredService<IOptions<CollectionOptions>>().Value.Read());
        services.TryAddSingleton<RescanNoticeBoard>();
        services.TryAddSingleton<CollectionBoost>();
        services.TryAddSingleton(new AppEventHub());
        services.TryAddSingleton<IAppEventPublisher>(provider =>
            provider.GetRequiredService<AppEventHub>());
        services.AddHostedService<LocalAccountBootstrap>();
        services.AddHostedService<OidcDiscoveryProbe>();
        services.AddHostedService<DriverConnectionSupervisor>();
        services.AddHostedService<AppEventHubLifetime>();
        services.AddHostedService(provider => provider.GetRequiredService<ScanRunner>());
        services.AddHostedService<EpgCollector>();
        services.AddHostedService<RideAlongHarvester>();

        return services;
    }
}
