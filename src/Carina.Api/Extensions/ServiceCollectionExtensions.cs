using Carina.Api.Services;

namespace Carina.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<DriverStatusService>();
        services.AddScoped<DriverRestartService>();
        services.AddScoped<TunerLedgerService>();
        services.AddScoped<ScanService>();
        services.AddScoped<ChannelCatalogService>();
        services.AddScoped<CollectionStatusService>();
        services.AddScoped<CollectionBoostService>();
        services.AddScoped<EpgRebuildService>();
        services.AddScoped<ProgrammeGuideService>();

        return services;
    }
}
