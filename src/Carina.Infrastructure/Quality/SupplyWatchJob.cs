using Carina.Domain.Quality;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed class SupplyWatchJob(
    IServiceScopeFactory scopes,
    QualitySignalSettings settings,
    TimeProvider clock,
    ILogger<SupplyWatchJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstRollup;

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(waiting, clock, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            waiting = settings.BetweenRollups;

            try
            {
                await WatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A supply watch failed; the next one is unaffected.");
            }
        }
    }

    private async Task WatchAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<SupplyWatchRound>().WatchAsync(cancellationToken);
    }
}
