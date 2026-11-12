using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Notifications;
using Carina.Domain.Programmes;

using Microsoft.Extensions.Logging;

namespace Carina.Infrastructure.Notifications;

/// <summary>
/// One look at the programmes asked to be told of: each row is turned by the time and by what the guide says,
/// and what there is to tell of it is taken as a notice for the destinations the row names.
/// </summary>
/// <remarks>
/// The notice, its deliveries and the row's new state are written together or not at all, in the turn the writers
/// of the destinations take, and the row is read again inside that turn. These notices pass by the sets of rules.
/// A row that cannot be turned is logged and the others of the same look are turned.
/// </remarks>
public sealed class ProgrammeWatchRound(
    IProgrammeWatchRepository watches,
    IAnnouncedProgrammes programmes,
    IBroadcastServiceRepository stations,
    INoticeRepository notices,
    INotificationRuleRepository rules,
    INotificationDestinationRepository destinations,
    IAtomicWrite writes,
    NoticeLinkBase links,
    TimeProvider clock,
    ILogger<ProgrammeWatchRound> logger)
{
    public async Task<NotificationIntake> TakeAsync(CancellationToken cancellationToken)
    {
        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<ProgrammeWatch> followed = await watches.ListFollowedAsync(now, cancellationToken);

        if (followed.Count is 0)
        {
            return new NotificationIntake(0, 0, 0);
        }

        IReadOnlySet<NoticeKey> told = await notices.HeldAmongAsync(
            [.. followed.Select(watch => watch.Id.Value)],
            cancellationToken);
        int taken = 0;
        int queued = 0;

        foreach (ProgrammeWatch watch in followed)
        {
            int? delivered = await LookAtAsync(watch, told, now, cancellationToken);

            taken += delivered is null ? 0 : 1;
            queued += delivered ?? 0;
        }

        return new NotificationIntake(followed.Count, taken, queued);
    }

    private static IReadOnlySet<NoticeKind> ToldOf(ProgrammeWatchId watch, IReadOnlySet<NoticeKey> told)
        => told.Where(key => key.SourceId == watch.Value).Select(key => key.Kind).ToHashSet();

    private async Task<int?> LookAtAsync(
        ProgrammeWatch watch,
        IReadOnlySet<NoticeKey> told,
        DateTime now,
        CancellationToken cancellationToken)
    {
        try
        {
            WatchedGuide guide = await GuideOfAsync(watch, cancellationToken);
            ProgrammeWatchStep foreseen = ProgrammeWatchLook.At(watch, guide, ToldOf(watch.Id, told), now);

            if (foreseen == ProgrammeWatchStep.Nothing)
            {
                return null;
            }

            return await writes.AllOrNothingAsync(inner => TurnAsync(watch.Id, guide, now, inner), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception failure)
        {
            logger.LogError(
                failure,
                "The programme asked to be told of in row {Watch} could not be turned; the others of this look are unaffected.",
                watch.Id.Value);

            return null;
        }
    }

    private async Task<WatchedGuide> GuideOfAsync(ProgrammeWatch watch, CancellationToken cancellationToken)
    {
        DateTime? heardWholeAt = await programmes.HeardWholeAtAsync(
            watch.NetworkId.Value,
            watch.ServiceId.Value,
            cancellationToken);
        Programme? announced = await programmes.FindAsync(watch.Programme, cancellationToken);
        BroadcastService? station = await stations.FindAsync(watch.NetworkId, watch.ServiceId, cancellationToken);

        return WatchedGuide.Of(announced, heardWholeAt, station?.Name);
    }

    private async Task<int?> TurnAsync(
        ProgrammeWatchId id,
        WatchedGuide guide,
        DateTime now,
        CancellationToken cancellationToken)
    {
        await rules.TakeTurnAsync(cancellationToken);

        ProgrammeWatch? watch = await watches.FindAsync(id, cancellationToken);

        if (watch is null)
        {
            return null;
        }

        IReadOnlySet<NoticeKey> told = await notices.HeldAmongAsync([id.Value], cancellationToken);
        ProgrammeWatchStep step = ProgrammeWatchLook.At(watch, guide, ToldOf(id, told), now);

        if (step.Changed)
        {
            await watches.SaveAsync(watch, cancellationToken);
        }

        return step.Tells is { } kind ? await TellAsync(watch, kind, now, cancellationToken) : null;
    }

    private async Task<int> TellAsync(
        ProgrammeWatch watch,
        NoticeKind kind,
        DateTime now,
        CancellationToken cancellationToken)
    {
        Notice notice = Notice.Take(
            NoticeId.New(),
            kind,
            watch.Id.Value,
            now,
            NoticeWording.Of(ProgrammeWatchNoticeSource.Of(watch, kind, now).Happening(), links),
            now);
        List<NotificationDelivery> deliveries =
        [
            .. NoticeRouting.DestinationsFor(watch, await destinations.ListAsync(cancellationToken))
                .Select(destination => NotificationDelivery.Queue(NotificationDeliveryId.New(), notice.Id, destination, now)),
        ];

        if (!await notices.AddAsync(notice, deliveries, cancellationToken))
        {
            throw new InvalidOperationException($"A {kind} notice of row {watch.Id.Value} is already held.");
        }

        return deliveries.Count;
    }
}
