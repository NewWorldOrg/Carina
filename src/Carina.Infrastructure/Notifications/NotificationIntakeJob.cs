using Carina.Domain.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The loop that reads the ledgers for what happened, and the programmes asked to be told of, and takes what there
/// is to tell into the notice ledger, one look at a time. Either of the two may fail without stopping the other.
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

            await LookAsync(
                provider => provider.GetRequiredService<NotificationIntakeRound>().TakeAsync(stoppingToken),
                stoppingToken);
            await LookAsync(
                provider => provider.GetRequiredService<ProgrammeWatchRound>().TakeAsync(stoppingToken),
                stoppingToken);
        }
    }

    private async Task LookAsync(Func<IServiceProvider, Task<NotificationIntake>> take, CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            NotificationIntake took = await take(scope.ServiceProvider);

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
        }
        catch (Exception failure)
        {
            logger.LogError(failure, "A look for what to give notice of failed; the next one is unaffected.");
        }
    }
}
