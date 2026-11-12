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
    NotificationIntakeDomainService intake,
    INoticeRepository notices,
    INotificationRuleRepository rules,
    INotificationDestinationRepository destinations,
    NotificationIntakeSettings settings,
    NoticeLinkBase links,
    ILogger<NotificationIntakeRound> logger)
{
    public async Task<NotificationIntake> TakeAsync(CancellationToken cancellationToken)
    {
        NoticeHappenings found = await intake.FreshAsync(settings.LooksBack, cancellationToken);
        DateTime now = found.At;

        if (found.Fresh.Count is 0)
        {
            return new NotificationIntake(found.Seen, 0, 0);
        }

        IReadOnlyList<NotificationRule> ruleSets = await rules.ListAsync(cancellationToken);
        IReadOnlyList<NotificationDestination> known = await destinations.ListAsync(cancellationToken);
        int taken = 0;
        int queued = 0;

        foreach (NoticeHappening happening in found.Fresh)
        {
            int? delivered = await TakeAsync(happening, ruleSets, known, now, cancellationToken);

            taken += delivered is null ? 0 : 1;
            queued += delivered ?? 0;
        }

        return new NotificationIntake(found.Seen, taken, queued);
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
