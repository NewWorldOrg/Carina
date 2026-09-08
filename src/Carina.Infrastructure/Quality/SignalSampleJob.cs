using Carina.Domain.Quality;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Quality;

public sealed class SignalSampleJob(
    IServiceScopeFactory scopes,
    QualitySignalSettings settings,
    TimeProvider clock,
    ILogger<SignalSampleJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstSample;

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

            waiting = settings.BetweenSamples;

            try
            {
                await TakeAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A round of signal sampling failed; the next one is unaffected.");
            }
        }
    }

    private async Task TakeAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<SignalSampleRound>().TakeAsync(cancellationToken);
    }
}
