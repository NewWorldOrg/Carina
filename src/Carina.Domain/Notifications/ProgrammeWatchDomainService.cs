using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Notifications;

/// <summary>
/// A programme of the guide, judged fit to be asked to be told of: what the guide and the station say of it, the
/// minutes before it begins that it is told of, and when it was judged.
/// </summary>
public sealed record ProgrammeWatchCandidate(ProgrammeId Programme, WatchedBroadcast Broadcast, int LeadMinutes, DateTime At);

/// <summary>
/// A programme judged fit to be asked for, or why it is not.
/// </summary>
public sealed record ProgrammeWatchConsidered(ProgrammeWatchCandidate? Candidate, ProgrammeWatchRefusal? Refusal);

/// <summary>
/// The row made for a programme asked for, with the settled row of the same programme it takes the place of, or why
/// no row is made.
/// </summary>
public sealed record ProgrammeWatchPlaced(ProgrammeWatch? Watch, ProgrammeWatch? Replaced, ProgrammeWatchRefusal? Refusal);

/// <summary>
/// Decides whether a programme may be asked to be told of, looking it up in the guide, and then which destinations it
/// is told to and whether a row of the same programme stands in its way.
/// </summary>
public sealed class ProgrammeWatchDomainService(
    IAnnouncedProgrammes programmes,
    IBroadcastServiceRepository stations,
    INotificationSettingsRepository settings,
    INotificationDestinationRepository destinations,
    IProgrammeWatchRepository watches,
    TimeProvider clock)
{
    public async Task<ProgrammeWatchConsidered> ConsiderAsync(
        ProgrammeId programme,
        DateTime startsAt,
        int? leadMinutes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(programme);

        Programme? announced = await programmes.FindAsync(programme, cancellationToken);

        if (announced is null || announced.StartsAt != startsAt)
        {
            return new ProgrammeWatchConsidered(null, ProgrammeWatchRefusal.NoSuchProgramme);
        }

        DateTime now = clock.GetUtcNow().UtcDateTime;
        int lead = leadMinutes ?? NotificationSettings.LeadMinutesOver(await settings.ReadAsync(cancellationToken));

        if (ProgrammeWatch.RefusalToAsk(announced, lead, now) is { } refusal)
        {
            return new ProgrammeWatchConsidered(null, refusal);
        }

        BroadcastService? station = await stations.FindAsync(programme.NetworkId, programme.ServiceId, cancellationToken);

        return new ProgrammeWatchConsidered(
            new ProgrammeWatchCandidate(programme, WatchedBroadcast.Of(announced, station?.Name), lead, now),
            null);
    }

    /// <summary>
    /// Makes the row of a programme judged fit, told to the destinations named or, with none named, to every one turned
    /// on. A row of the same programme that waits, or was told of and is not over, stands in its way; any other row of it
    /// is handed back to be replaced.
    /// </summary>
    public async Task<ProgrammeWatchPlaced> PlaceAsync(
        ProgrammeWatchCandidate candidate,
        IReadOnlyList<Guid>? named,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        IReadOnlyList<NotificationDestination> held = await destinations.ListAsync(cancellationToken);

        if (named is null && !held.Any(destination => destination.Enabled))
        {
            return new ProgrammeWatchPlaced(null, null, ProgrammeWatchRefusal.NoDestinationTurnedOn);
        }

        if (ToldTo(named, held) is not { } toldTo)
        {
            return new ProgrammeWatchPlaced(null, null, ProgrammeWatchRefusal.DestinationsNotTurnedOn);
        }

        ProgrammeWatch? before = await watches.FindOfAsync(candidate.Programme, cancellationToken);

        if (before is not null && before.StandsAt(candidate.At))
        {
            return new ProgrammeWatchPlaced(null, null, ProgrammeWatchRefusal.AlreadyAsked);
        }

        ProgrammeWatch watch = ProgrammeWatch.Ask(
            ProgrammeWatchId.New(),
            candidate.Programme,
            candidate.Broadcast,
            candidate.LeadMinutes,
            toldTo,
            candidate.At);

        return new ProgrammeWatchPlaced(watch, before, null);
    }

    private static IReadOnlyList<NotificationDestinationId>? ToldTo(
        IReadOnlyList<Guid>? named,
        IReadOnlyList<NotificationDestination> held)
    {
        IReadOnlyList<Guid> turnedOn = [.. held.Where(destination => destination.Enabled).Select(destination => destination.Id.Value)];
        IReadOnlyList<Guid> toldTo = named ?? turnedOn;

        return toldTo.Count > 0 && toldTo.All(turnedOn.Contains)
            ? [.. toldTo.Distinct().Select(id => new NotificationDestinationId(id))]
            : null;
    }
}
