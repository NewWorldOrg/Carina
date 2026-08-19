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
        services.AddScoped<ScanService>();
        services.AddScoped<ChannelCatalogService>();
        services.AddScoped<CollectionStatusService>();
        services.AddScoped<CollectionBoostService>();
        services.AddScoped<EpgRebuildService>();
        services.AddScoped<ProgrammeGuideService>();
        services.AddScoped<ProgrammeFeedService>();
        services.AddScoped<ArchiveService>();

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
