using Carina.Domain.Quality;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed class QualityThresholdMeasureJob(
    IServiceScopeFactory scopes,
    QualitySignalSettings settings,
    TimeProvider clock,
    ILogger<QualityThresholdMeasureJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstThresholdMeasure;

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

            waiting = settings.BetweenThresholdMeasures;

            try
            {
                await MeasureAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "Measuring the signal levels failed; the next measure is unaffected.");
            }
        }
    }

    private async Task MeasureAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<QualityThresholdMeasureRound>().RunAsync(cancellationToken);
    }
}
