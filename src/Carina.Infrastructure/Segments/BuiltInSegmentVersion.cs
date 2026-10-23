using Carina.Domain.Segments;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Segments;

/// <summary>
/// Keeps the built-in version of this release (<see cref="SegmentModel.BuiltIn"/>) when the application starts,
/// before detection, learning or the chapters of an encode ask for the version in use. With no version kept it is
/// kept as the first and put in use; beside an earlier built-in version in use that was not chosen by hand it takes
/// its place; beside a version chosen by hand or learned it waits as a candidate
/// (<see cref="SegmentModelVersion.KeepBuiltIn"/>). Kept already, nothing is written. A start that cannot reach the
/// store says so and goes on; the version is kept at the next start.
/// </summary>
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
