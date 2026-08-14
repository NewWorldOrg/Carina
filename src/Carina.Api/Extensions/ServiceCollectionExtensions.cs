using Carina.Api.Services;

namespace Carina.Api.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddApplicationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<DriverStatusService>();
        services.AddScoped<TunerLedgerService>();
        services.AddScoped<ScanService>();

        return services;
    }
}
