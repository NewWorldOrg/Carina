using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

public sealed class BuiltInSegmentVersion(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<BuiltInSegmentVersion> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            ISegmentModelVersionRepository versions = scope.ServiceProvider.GetRequiredService<ISegmentModelVersionRepository>();

            if (await versions.KeepBuiltInAsync(SegmentModel.BuiltIn, clock.GetUtcNow().UtcDateTime, cancellationToken) is { } kept)
            {
                logger.LogInformation(
                    "The built-in version of the model was kept as version {Version}; version {Active} is in use.",
                    kept.Added.Number,
                    kept.Active.Number);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "The built-in version of the model could not be kept; it is kept at the next start.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
