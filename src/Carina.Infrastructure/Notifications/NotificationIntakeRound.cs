using Carina.Domain.Notifications;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Notifications;

public sealed record NotificationIntake(int Seen, int Taken, int Queued);

/// <summary>
/// One look at the ledgers for what happened since the look-back began, taking each happening the
/// notice ledger does not hold yet together with a delivery for every destination the rule sets name.
/// </summary>
/// <remarks>
/// What happened before the look-back began is left alone. A happening no rule set asks for is still
/// taken, with nothing to deliver. A notice that cannot be written is logged and the others of the
/// same look are written.
/// </remarks>
public sealed class NotificationIntakeRound(
    INoticeSourceReader sources,
    INoticeRepository notices,
    INotificationRuleRepository rules,
    INotificationDestinationRepository destinations,
    NotificationIntakeSettings settings,
    NoticeLinkBase links,
    TimeProvider clock,
    ILogger<NotificationIntakeRound> logger)
{
    public async Task<NotificationIntake> TakeAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        DateTime since = now - settings.LooksBack;

        List<NoticeHappening> happened =
        [
            .. (await sources.SinceAsync(since, cancellationToken))
                .SelectMany(source => source.Happenings())
                .Where(happening => happening.OccurredAt >= since && happening.OccurredAt <= now)
                .OrderBy(happening => happening.OccurredAt),
        ];

        if (happened.Count is 0)
        {
            return new NotificationIntake(0, 0, 0);
        }

        IReadOnlySet<NoticeKey> held = await notices.HeldAmongAsync(
            [.. happened.Select(happening => happening.SourceId).Distinct()],
            cancellationToken);
        List<NoticeHappening> fresh = [.. happened.Where(happening => !held.Contains(happening.Key))];

        if (fresh.Count is 0)
        {
            return new NotificationIntake(happened.Count, 0, 0);
        }

        IReadOnlyList<NotificationRule> ruleSets = await rules.ListAsync(cancellationToken);
        IReadOnlyList<NotificationDestination> known = await destinations.ListAsync(cancellationToken);
        int taken = 0;
        int queued = 0;

        foreach (NoticeHappening happening in fresh)
        {
            int? delivered = await TakeAsync(happening, ruleSets, known, now, cancellationToken);

            taken += delivered is null ? 0 : 1;
            queued += delivered ?? 0;
        }

        return new NotificationIntake(happened.Count, taken, queued);
    }

    private async Task<int?> TakeAsync(
        NoticeHappening happening,
        IReadOnlyList<NotificationRule> ruleSets,
        IReadOnlyList<NotificationDestination> known,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            Notice notice = Notice.Take(
                NoticeId.New(),
                happening.Kind,
                happening.SourceId,
                happening.OccurredAt,
                NoticeWording.Of(happening, links),
                now);
            List<NotificationDelivery> deliveries =
            [
                .. NoticeRouting.DestinationsFor(happening.Kind, ruleSets, known)
                    .Select(destination => NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, now)),
            ];

            return await notices.AddAsync(notice, deliveries, cancellationToken) ? deliveries.Count : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "A {Kind} notice from row {Source} could not be taken; the others of this look are unaffected.",
                happening.Kind,
                happening.SourceId);

            return null;
        }
    }
}
