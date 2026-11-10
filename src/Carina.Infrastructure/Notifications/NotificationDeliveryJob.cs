using Carina.Domain.Notifications;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// The loop that sends the notices that wait, one pass at a time, and now and then clears away the notices, and the
/// settled programmes asked to be told of, too old to keep.
/// </summary>
public sealed class NotificationDeliveryJob(
    IServiceScopeFactory scopes,
    NotificationDeliverySettings settings,
    TimeProvider clock,
    ILogger<NotificationDeliveryJob> logger) : BackgroundService
{
    private DateTime? purgedAt;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan waiting = settings.BeforeFirstRound;

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

            waiting = settings.BetweenRounds;

            try
            {
                await PassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception failure)
            {
                logger.LogError(failure, "A pass over the notices that wait failed; the next one is unaffected.");
            }
        }
    }

    private async Task PassAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();

        NotificationDeliveries moved = await scope.ServiceProvider
            .GetRequiredService<NotificationDeliveryRound>()
            .SendAsync(cancellationToken);

        if (moved.Moved > 0)
        {
            logger.LogInformation(
                "Sent {Sent} notice(s); {Failed} attempt(s) failed and {GivenUp} delivery(ies) were given up.",
                moved.Sent,
                moved.Failed,
                moved.GivenUp);
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;

        if (purgedAt is { } last && now - last < settings.BetweenPurges)
        {
            return;
        }

        purgedAt = now;

        int removed = await scope.ServiceProvider
            .GetRequiredService<INoticeRepository>()
            .RemoveBeforeAsync(now - settings.Keeps, cancellationToken);
        int forgotten = await scope.ServiceProvider
            .GetRequiredService<IProgrammeWatchRepository>()
            .RemoveSettledBeforeAsync(now - settings.Keeps, cancellationToken);

        if (removed + forgotten > 0)
        {
            logger.LogInformation(
                "Cleared away {Removed} notice(s) and {Forgotten} programme(s) asked to be told of, too old to keep.",
                removed,
                forgotten);
        }
    }
}
