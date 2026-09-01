using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Driver;
using Carina.Domain.DriverStatus;
using Carina.Domain.Events;
using Carina.Domain.Integrity;
using Carina.Domain.Playback;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;
using Carina.Domain.Rules;
using Carina.Domain.Scans;
using Carina.Domain.Streaming;
using Carina.Domain.Thumbnails;
using Carina.Infrastructure.Auth;
using Carina.Infrastructure.Channels;
using Carina.Infrastructure.Collection;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Driver;
using Carina.Infrastructure.Events;
using Carina.Infrastructure.Integrity;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;
using Carina.Infrastructure.Playback;
using Carina.Infrastructure.Programmes;
using Carina.Infrastructure.Recordings;
using Carina.Infrastructure.Reservations;
using Carina.Infrastructure.Rules;
using Carina.Infrastructure.Scanning;
using Carina.Infrastructure.Streaming;
using Carina.Infrastructure.Thumbnails;

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

        services.AddSingleton<IValidateOptions<IntegrityOptions>, IntegrityValidation>();
        services.AddOptions<IntegrityOptions>()
            .Configure(options => options.ReadFrom(configuration))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<RecordingOptions>, RecordingValidation>();
        services.AddOptions<RecordingOptions>()
            .Configure(options => options.ReadFrom(configuration))
            .ValidateOnStart();

        services.AddSingleton<IValidateOptions<ThumbnailOptions>, ThumbnailValidation>();
        services.AddOptions<ThumbnailOptions>()
            .Configure(options => options.ReadFrom(configuration))
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
        services.AddScoped<ProgrammeSearchScope>();
        services.AddScoped<IStreamVisitRepository, StreamVisitRepository>();
        services.AddScoped<ICollectionEpochRepository, CollectionEpochRepository>();
        services.AddScoped<IArchivedProgrammeRepository, ArchivedProgrammeRepository>();
        services.AddScoped<ICandidateChannelRepository, CandidateChannelRepository>();
        services.AddScoped<ISatelliteTransportStreamRepository, SatelliteTransportStreamRepository>();
        services.AddScoped<IScanRunRepository, ScanRunRepository>();
        services.AddScoped<IReservationRepository, ReservationRepository>();
        services.AddScoped<IReservationOutcomeRepository, ReservationOutcomeRepository>();
        services.AddScoped<IReservationRecordingContract, ReservationRecordingContract>();
        services.AddScoped<IRecordingLedger, RecordingLedger>();
        services.AddScoped<IIntegrityCheckRepository, IntegrityCheckRepository>();
        services.AddScoped<IThumbnailWorklist, ThumbnailWorklist>();
        services.AddScoped<IChannelScanOrchestrator, ChannelScanOrchestrator>();
        services.AddScoped<ScanApplier>();
        services.AddScoped<IBroadcastStreamDirectory, BroadcastStreamDirectory>();
        services.AddScoped<ITunerCapacityDirectory, TunerCapacityDirectory>();
        services.AddScoped<IServiceTuningDirectory, ServiceTuningDirectory>();
        services.AddScoped<IServiceReachSettingsRepository, ServiceReachSettingsRepository>();
        services.AddScoped<ITuneFailureReporter, CandidateTuneFailureReporter>();
        services.AddScoped<ReservationSchedulingService>();
        services.AddScoped<ReservationOutcomeService>();
        services.AddScoped<RuleMatcher>();
        services.AddScoped<IRuleRepository, RuleRepository>();
        services.AddScoped<RuleApplicationService>();
        services.AddScoped<ProgrammeWriter>();
        services.AddScoped<StreamVisitor>();
        services.AddScoped<CollectionRound>();
        services.AddScoped<ArchiveTransfer>();

        services.AddSingleton(TimeProvider.System);
        services.AddHttpClient<IOidcGateway, OidcGateway>();
        services.TryAddSingleton(new RuleApplicationSettings());
        services.TryAddSingleton(new RecalculationSettings());
        services.TryAddSingleton(new ReservationOutcomeSettings());
        services.AddSingleton<ReservationRecalculationHostedService>();
        services.TryAddSingleton<IRecalculationNotice>(provider =>
            provider.GetRequiredService<ReservationRecalculationHostedService>());
        services.TryAddSingleton<IRecalculationPass>(provider =>
            provider.GetRequiredService<ReservationRecalculationHostedService>());
        services.TryAddSingleton(new RuleApplySettings());
        services.TryAddSingleton<RuleApplyNow>();
        services.TryAddSingleton(SessionPolicy.Default);
        services.TryAddSingleton(PasswordHashPolicy.Default);
        services.TryAddSingleton(LoginRatePolicy.Default);
        services.TryAddSingleton(OidcLoginPolicy.Default);
        services.TryAddSingleton<OidcDirectoryCache>();
        services.TryAddSingleton<IOidcReachability, OidcReachability>();
        services.TryAddSingleton<IPendingOidcLoginStore, PendingOidcLoginStore>();
        services.TryAddSingleton(PlaybackTicketPolicy.Default);
        services.TryAddSingleton<IPlaybackTicketStore, PlaybackTicketStore>();
        services.TryAddSingleton(PlaybackGrantPolicy.Default);
        services.TryAddSingleton<IPlaybackGrantStore, PlaybackGrantStore>();
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
        services.TryAddSingleton(StorageMonitorSettings.Default);
        services.TryAddSingleton<StorageMonitor>();
        services.TryAddSingleton<DiskPrecheckService>();
        services.TryAddSingleton<RecordingSettings>(provider =>
            provider.GetRequiredService<IOptions<RecordingOptions>>().Value.Read());
        services.TryAddSingleton(ScanSettings.Default);
        services.TryAddSingleton(RollingHorizon.Default);
        services.TryAddSingleton<IntegritySettings>(provider =>
            provider.GetRequiredService<IOptions<IntegrityOptions>>().Value.Read());
        services.TryAddSingleton<IRecordingFileSurvey, LocalRecordingFileSurvey>();
        services.TryAddSingleton<IPlaybackFileStore, LocalPlaybackFileStore>();
        services.AddSingleton<IntegrityCheckJob>();
        services.TryAddSingleton<ThumbnailSettings>(provider =>
            provider.GetRequiredService<IOptions<ThumbnailOptions>>().Value.Read());
        services.TryAddSingleton<IThumbnailRenderer, FfmpegThumbnailRenderer>();
        services.AddScoped<IScrubFrames, Scrubber>();
        services.AddScoped<IDrawnThumbnails, DrawnThumbnails>();
        services.TryAddSingleton(new StreamAttributeSettings());
        services.TryAddSingleton(new LiveTranscodeSettings());
        services.TryAddSingleton(new OnTheFlySettings());
        services.TryAddSingleton(new LiveWireSettings());
        services.TryAddSingleton<ILiveWireSource, NoLiveSource>();
        services.TryAddSingleton<IStreamAttributeReader, FfprobeStreamAttributeReader>();
        services.TryAddSingleton<ILiveEncoderSelector>(provider => new LiveEncoderSelection(
            provider.GetRequiredService<LiveTranscodeSettings>(),
            provider.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IOnTheFlyPlayer, OnTheFlyPlayer>();
        services.AddSingleton<ThumbnailJob>();
        services.TryAddSingleton<IThumbnailRemaker>(provider =>
            provider.GetRequiredService<ThumbnailJob>());
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
        services.AddHostedService(provider => provider.GetRequiredService<IntegrityCheckJob>());
        services.AddHostedService(provider => provider.GetRequiredService<ThumbnailJob>());
        services.AddHostedService(provider =>
            provider.GetRequiredService<ReservationRecalculationHostedService>());

        services.AddCarinaRecording();

        return services;
    }
}
