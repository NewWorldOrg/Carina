using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.Extensions.DependencyInjection;

namespace Carina.Infrastructure.DependencyInjection;

public static class RecordingDirectoryRegistration
{
    public static IServiceCollection AddRecordingDirectory(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRecordingDirectory, RecordingDirectory>();

        return services;
    }
}
