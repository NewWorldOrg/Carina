using Carina.Domain.Recordings;
using Carina.Infrastructure.Configuration;
using Carina.Infrastructure.Persistence.Repositories;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Carina.Infrastructure.Recordings;

public static class RecordingServices
{
    public static IServiceCollection AddCarinaRecording(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IRecordingRepository, RecordingRepository>();
        services.AddScoped<IRecordingDirectory, RecordingDirectory>();
        services.AddScoped<ProgramExtensionFollower>();
        services.TryAddSingleton<EndsAlreadyAsked>();
        services.AddScoped<RecordingRefusalReporter>();
        services.TryAddSingleton<RetryPolicy>(provider =>
            provider.GetRequiredService<IOptions<RecordingRetryOptions>>().Value.Read());
        services.AddScoped<RecordingRetries>();
        services.AddScoped<RecordingRound>();
        services.TryAddSingleton(RecordingWatchSettings.Default);
        services.TryAddSingleton<IRecordingFileWeigher, LocalRecordingFileWeigher>();
        services.TryAddSingleton<IRecordingFileEraser, DriverRecordingFileEraser>();
        services.AddSingleton<RecordingStreamSupervisor>();
        services.AddHostedService<RecordingTickJob>();
        services.AddHostedService<RecordingStreamJob>();

        return services;
    }
}
