using Carina.Domain.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The loop that reads the ledgers for what happened and takes it into the notice ledger, one look at a time.
/// </summary>
public sealed class NotificationIntakeJob(
    IServiceScopeFactory scopes,
    NotificationIntakeSettings settings,
    TimeProvider clock,
    ILogger<NotificationIntakeJob> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
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
                NotificationIntake took = await TakeAsync(stoppingToken);

                if (took.Taken > 0)
                {
                    logger.LogInformation(
                        "Took {Taken} notice(s) and queued {Queued} delivery(ies).",
                        took.Taken,
                        took.Queued);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A look for what to give notice of failed; the next one is unaffected.");
            }
        }
    }

    private async Task<NotificationIntake> TakeAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        return await scope.ServiceProvider.GetRequiredService<NotificationIntakeRound>().TakeAsync(cancellationToken);
    }
}
