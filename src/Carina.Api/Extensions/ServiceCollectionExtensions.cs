using System.Net;

using Carina.Api.Authentication;
using Carina.Api.Services;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Options;

using ForwardedHeaders = Microsoft.AspNetCore.HttpOverrides.ForwardedHeaders;

namespace Carina.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<LocalAccountService>();
        services.AddScoped<AuthSessionService>();
        services.AddScoped<OidcLoginService>();
        services.AddScoped<OidcConfigService>();
        services.AddScoped<HealthService>();
        services.AddScoped<DriverStatusService>();
        services.AddScoped<DriverRestartService>();
        services.AddScoped<TunerLedgerService>();
        services.AddScoped<TunerHealthService>();
        services.AddScoped<ScanService>();
        services.AddScoped<ChannelCatalogService>();
        services.AddScoped<CollectionStatusService>();
        services.AddScoped<CollectionBoostService>();
        services.AddScoped<EpgRebuildService>();
        services.AddScoped<ProgrammeGuideService>();
        services.AddScoped<ProgrammeFeedService>();
        services.AddScoped<ArchiveService>();
        services.AddScoped<ReservationService>();
        services.AddScoped<RuleService>();
        services.AddSingleton<RecordingDeletions>();
        services.AddScoped<RecordingService>();
        services.AddScoped<IntegrityService>();
        services.AddScoped<PlaybackService>();
        services.AddScoped<StorageService>();
        services.AddSingleton<PlaybackTicketGate>();

        return services;
    }

    public static IServiceCollection AddTrustedProxies(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<ProxyTrustOptions>, ProxyTrustValidation>();
        services.AddOptions<ProxyTrustOptions>()
            .Configure(options =>
            {
                options.KnownProxies = configuration[TrustedProxies.ProxiesKey];
                options.KnownNetworks = configuration[TrustedProxies.NetworksKey];
            })
            .ValidateOnStart();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<ProxyTrustOptions>>().Value.Read());

        services.AddOptions<ForwardedHeadersOptions>().Configure<TrustedProxies>(Trusting);
        services.AddHostedService<TrustedProxyDiagnosis>();

        return services;
    }

    public static IServiceCollection AddPublicOrigin(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<PublicOriginOptions>, PublicOriginValidation>();
        services.AddOptions<PublicOriginOptions>()
            .Configure(options => options.Origin = configuration[PublicOrigin.Key])
            .ValidateOnStart();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<PublicOriginOptions>>().Value.Read());

        services.AddHostedService<PublicOriginDiagnosis>();

        return services;
    }

    public static IServiceCollection AddAnonymousNetworks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<IValidateOptions<AnonymousNetworkOptions>, AnonymousNetworkValidation>();
        services.AddOptions<AnonymousNetworkOptions>()
            .Configure(options => options.Networks = configuration[AnonymousNetworks.Key])
            .ValidateOnStart();

        services.AddSingleton(provider =>
            provider.GetRequiredService<IOptions<AnonymousNetworkOptions>>().Value.Read());

        services.AddHostedService<AnonymousNetworkDiagnosis>();

        return services;
    }

    private static void Trusting(ForwardedHeadersOptions options, TrustedProxies trusted)
    {
        options.ForwardedHeaders = trusted.TrustsNothing
            ? ForwardedHeaders.None
            : ForwardedHeaders.XForwardedFor
              | ForwardedHeaders.XForwardedProto
              | ForwardedHeaders.XForwardedHost;
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();

        foreach (IPAddress proxy in trusted.Proxies)
        {
            options.KnownProxies.Add(proxy);
        }

        foreach (IPNetwork network in trusted.Networks)
        {
            options.KnownIPNetworks.Add(network);
        }
    }
}
