using Carina.Domain.Quality;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed class QualitySignalRollupJob(
    IServiceScopeFactory scopes,
    QualitySignalSettings settings,
    TimeProvider clock,
    ILogger<QualitySignalRollupJob> logger) : BackgroundService
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
                Report(await SweepAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A signal rollup failed; the next one is unaffected.");
            }
        }
    }

    private async Task<QualitySignalSweep> SweepAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<QualitySignalRollupRound>().RunAsync(cancellationToken);
    }

    private void Report(QualitySignalSweep sweep)
    {
        if (sweep.Rolled is 0 && sweep.SamplesForgotten is 0 && sweep.WindowsForgotten is 0)
        {
            return;
        }

        logger.LogInformation(
            "A signal rollup wrote {Rolled} window(s), let go of {Samples} sample(s) that had been rolled up "
            + "and {Windows} window(s) past their retention.",
            sweep.Rolled,
            sweep.SamplesForgotten,
            sweep.WindowsForgotten);
    }
}
