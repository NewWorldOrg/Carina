using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.Recordings;

public static class RecordingServices
{
    public static IServiceCollection AddCarinaRecording(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRecordingRepository, RecordingRepository>();
        services.AddScoped<IRecordingDirectory, RecordingDirectory>();
        services.AddScoped<RecordingRound>();
        services.AddHostedService<RecordingTickJob>();

        return services;
    }
}
