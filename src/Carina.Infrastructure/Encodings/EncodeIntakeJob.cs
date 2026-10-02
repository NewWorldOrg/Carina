using Carina.Domain.Encodings;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// The loop that reads the recording ledger for what has ended and queues it, one look at a time.
/// </summary>
public sealed class EncodeIntakeJob(
    IServiceScopeFactory scopes,
    EncodeSettings settings,
    TimeProvider clock,
    ILogger<EncodeIntakeJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        bool told = false;
        TimeSpan waiting = settings.BeforeFirstLook;

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

            waiting = settings.BetweenLooks;

            try
            {
                EncodeIntake took = await TakeAsync(stoppingToken);

                if (!took.Automatically && !told)
                {
                    logger.LogInformation(
                        "A recording that ends is not being queued for encoding, because the auto-run is turned off.");
                }

                told = !took.Automatically;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A look for recordings to encode failed; the next one is unaffected.");
            }
        }
    }

    private async Task<EncodeIntake> TakeAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<EncodeIntakeRound>().TakeAsync(cancellationToken);
    }
}
