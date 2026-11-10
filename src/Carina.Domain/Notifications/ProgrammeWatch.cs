using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Reservations;

namespace Carina.Domain.Notifications;

/// <summary>
/// One programme somebody asked to be told of as it begins, without recording it. It holds no tuner and no
/// place in the schedule, and it is told to the destinations asked for with it rather than through the rules.
/// </summary>
public sealed class ProgrammeWatch
{
    private ProgrammeWatch()
    {
    }

    public ProgrammeWatchId Id { get; private set; } = null!;

    public NetworkId NetworkId { get; private set; } = null!;

    public ServiceId ServiceId { get; private set; } = null!;

    public EventId EventId { get; private set; } = null!;

    public ProgrammeId Programme => new(NetworkId, ServiceId, EventId);

    public DateTime StartsAt { get; private set; }

    public DateTime EndsAt { get; private set; }

    public string ProgrammeName { get; private set; } = string.Empty;

    public string? StationName { get; private set; }

    public int LeadMinutes { get; private set; }

    public IReadOnlyList<NotificationDestinationId> Destinations { get; private set; } = [];

    public ProgrammeWatchState State { get; private set; }

    public DateTime? NotifiedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime ChangedAt { get; private set; }

    private bool FollowsTheGuide => State is ProgrammeWatchState.Waiting or ProgrammeWatchState.Notified;

    public static ProgrammeWatch Ask(
        ProgrammeWatchId id,
        ProgrammeId programme,
        WatchedBroadcast announced,
        int leadMinutes,
        IReadOnlyList<NotificationDestinationId> destinations,
        DateTime at)
    {
        ArgumentNullException.ThrowIfNull(announced);

        if (at >= announced.StartsAt)
        {
            throw new InvalidOperationException("Only a programme that has not begun is asked to be told of.");
        }

        return Rehydrate(id, programme, announced, leadMinutes, destinations, ProgrammeWatchState.Waiting, null, at, at);
    }

    public static ProgrammeWatch Rehydrate(
        ProgrammeWatchId id,
        ProgrammeId programme,
        WatchedBroadcast announced,
        int leadMinutes,
        IReadOnlyList<NotificationDestinationId> destinations,
        ProgrammeWatchState state,
        DateTime? notifiedAt,
        DateTime createdAt,
        DateTime changedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(programme);
        ArgumentNullException.ThrowIfNull(announced);

        if (!NotificationSettings.LeadMinutesOffered.Contains(leadMinutes))
        {
            throw new ArgumentOutOfRangeException(
                nameof(leadMinutes),
                leadMinutes,
                $"A notice is sent {string.Join(", ", NotificationSettings.LeadMinutesOffered)} minutes before the programme.");
        }

        if (!Enum.IsDefined(state))
        {
            throw new ArgumentOutOfRangeException(nameof(state), state, "A programme asked to be told of is in a state the row holds.");
        }

        if (state is ProgrammeWatchState.Notified != notifiedAt is not null)
        {
            throw new ArgumentException("A programme was told of exactly when a time is kept for it.", nameof(notifiedAt));
        }

        WatchedBroadcast held = Held(announced);

        return new ProgrammeWatch
        {
            Id = id,
            NetworkId = programme.NetworkId,
            ServiceId = programme.ServiceId,
            EventId = programme.EventId,
            StartsAt = held.StartsAt,
            EndsAt = held.EndsAt,
            ProgrammeName = held.ProgrammeName,
            StationName = held.StationName,
            LeadMinutes = leadMinutes,
            Destinations = ToldTo(destinations, state),
            State = state,
            NotifiedAt = notifiedAt is { } told ? UtcTimes.Required(told, nameof(notifiedAt)) : null,
            CreatedAt = UtcTimes.Required(createdAt, nameof(createdAt)),
            ChangedAt = UtcTimes.Required(changedAt, nameof(changedAt)),
        };
    }

    public ProgrammeWatchMoment MomentAt(DateTime at)
    {
        if (at >= EndsAt)
        {
            return ProgrammeWatchMoment.Over;
        }

        if (at >= StartsAt)
        {
            return ProgrammeWatchMoment.OnAir;
        }

        return at >= StartsAt - TimeSpan.FromMinutes(LeadMinutes) ? ProgrammeWatchMoment.Soon : ProgrammeWatchMoment.NotYet;
    }

    /// <summary>
    /// Whether this row stands in the way of asking for the same programme again.
    /// </summary>
    public bool StandsAt(DateTime at)
        => State is ProgrammeWatchState.Waiting || (State is ProgrammeWatchState.Notified && at < EndsAt);

    /// <summary>
    /// Takes what the guide says now. Returns whether the start moved by more than the guide's usual drift.
    /// </summary>
    public bool Follow(WatchedBroadcast announced, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(announced);

        if (!FollowsTheGuide)
        {
            throw new InvalidOperationException($"A programme that is {State} no longer follows the guide.");
        }

        WatchedBroadcast now = Held(announced with
        {
            ProgrammeName = announced.ProgrammeName.Length > 0 ? announced.ProgrammeName : ProgrammeName,
            StationName = announced.StationName ?? StationName,
        });
        bool moved = (now.StartsAt - StartsAt).Duration() > EpgComparison.Unremarkable;

        if (now == new WatchedBroadcast(StartsAt, EndsAt, ProgrammeName, StationName))
        {
            return false;
        }

        StartsAt = now.StartsAt;
        EndsAt = now.EndsAt;
        ProgrammeName = now.ProgrammeName;
        StationName = now.StationName;
        ChangedAt = UtcTimes.Required(at, nameof(at));

        return moved;
    }

    public void Notify(DateTime at)
    {
        Settle(ProgrammeWatchState.Notified, at);
        NotifiedAt = ChangedAt;
    }

    public void Skip(DateTime at) => Settle(ProgrammeWatchState.Skipped, at);

    public void Cancel(DateTime at) => Settle(ProgrammeWatchState.Cancelled, at);

    /// <summary>
    /// Stops telling one destination of a programme that still waits. Left with none, it is cancelled.
    /// Returns whether the row changed.
    /// </summary>
    public bool StopTelling(NotificationDestinationId destination, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(destination);

        if (State is not ProgrammeWatchState.Waiting || !Destinations.Contains(destination))
        {
            return false;
        }

        Destinations = [.. Destinations.Where(told => !told.Equals(destination))];
        ChangedAt = UtcTimes.Required(at, nameof(at));

        if (Destinations.Count is 0)
        {
            State = ProgrammeWatchState.Cancelled;
        }

        return true;
    }

    private static WatchedBroadcast Held(WatchedBroadcast announced)
    {
        ArgumentNullException.ThrowIfNull(announced.ProgrammeName);

        if (UtcTimes.Required(announced.EndsAt, nameof(announced)) <= UtcTimes.Required(announced.StartsAt, nameof(announced)))
        {
            throw new ArgumentException("A programme asked to be told of ends after it begins.", nameof(announced));
        }

        if (announced.ProgrammeName.Length > Programmes.Programme.NameMaxLength)
        {
            throw new ArgumentException(
                $"A programme name is at most {Programmes.Programme.NameMaxLength} characters.",
                nameof(announced));
        }

        if (announced.StationName is { Length: > BroadcastService.NameMaxLength })
        {
            throw new ArgumentException($"A station name is at most {BroadcastService.NameMaxLength} characters.", nameof(announced));
        }

        return announced;
    }

    private static IReadOnlyList<NotificationDestinationId> ToldTo(
        IReadOnlyList<NotificationDestinationId> destinations,
        ProgrammeWatchState state)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        if (destinations.Any(destination => destination is null))
        {
            throw new ArgumentException("A programme is told of to destinations that are named.", nameof(destinations));
        }

        if (state is ProgrammeWatchState.Waiting && destinations.Count is 0)
        {
            throw new ArgumentException("A programme that waits is told of to at least one destination.", nameof(destinations));
        }

        return [.. destinations.Distinct()];
    }

    private void Settle(ProgrammeWatchState settled, DateTime at)
    {
        if (State is not ProgrammeWatchState.Waiting)
        {
            throw new InvalidOperationException($"A programme that is {State} does not become {settled}.");
        }

        State = settled;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }
}
