using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

/// <summary>
/// A set of rules for telling: which kinds of notice go to which destinations, while it is turned on.
/// </summary>
public sealed class NotificationRule
{
    public const int MostRules = 20;

    public const string UsualName = "失敗と異常";

    public static readonly IReadOnlyList<NoticeKind> UsualEvents =
    [
        NoticeKind.RecordingTruncated,
        NoticeKind.RecordingFailed,
        NoticeKind.EncodeFailed,
        NoticeKind.ReservationCompeting,
        NoticeKind.ReservationMissed,
        NoticeKind.ProgrammeMoved,
        NoticeKind.QualityIncidentDetected,
        NoticeKind.QualityIncidentResolved,
        NoticeKind.DeviceDisconnected,
    ];

    private NotificationRule()
    {
    }

    public NotificationRuleId Id { get; private set; } = null!;

    public NotificationName Name { get; private set; } = null!;

    public bool Enabled { get; private set; }

    public IReadOnlyList<NoticeKind> Events { get; private set; } = [];

    public IReadOnlyList<NotificationDestinationId> Destinations { get; private set; } = [];

    public DateTime CreatedAt { get; private set; }

    public DateTime ChangedAt { get; private set; }

    public static NotificationRule Define(
        NotificationRuleId id,
        NotificationName name,
        IReadOnlyList<NoticeKind> events,
        IReadOnlyList<NotificationDestinationId> destinations,
        DateTime at)
        => Rehydrate(id, name, enabled: true, events, destinations, at, at);

    /// <summary>
    /// The set of rules made with the first destination: failures and trouble, told to that destination.
    /// </summary>
    public static NotificationRule Usual(NotificationRuleId id, NotificationDestinationId destination, DateTime at)
        => Define(id, new NotificationName(UsualName), UsualEvents, [destination], at);

    public static NotificationRule Rehydrate(
        NotificationRuleId id,
        NotificationName name,
        bool enabled,
        IReadOnlyList<NoticeKind> events,
        IReadOnlyList<NotificationDestinationId> destinations,
        DateTime createdAt,
        DateTime changedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(name);

        return new NotificationRule
        {
            Id = id,
            Name = name,
            Enabled = enabled,
            Events = Told(events),
            Destinations = SentTo(destinations),
            CreatedAt = UtcTimes.Required(createdAt, nameof(createdAt)),
            ChangedAt = UtcTimes.Required(changedAt, nameof(changedAt)),
        };
    }

    public bool Covers(NoticeKind kind) => Enabled && Events.Contains(kind);

    public void Rename(NotificationName name, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(name);

        Name = name;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Tell(IReadOnlyList<NoticeKind> events, IReadOnlyList<NotificationDestinationId> destinations, DateTime at)
    {
        IReadOnlyList<NoticeKind> told = Told(events);
        IReadOnlyList<NotificationDestinationId> sentTo = SentTo(destinations);

        Events = told;
        Destinations = sentTo;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Enable(DateTime at)
    {
        Enabled = true;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    public void Disable(DateTime at)
    {
        Enabled = false;
        ChangedAt = UtcTimes.Required(at, nameof(at));
    }

    private static IReadOnlyList<NoticeKind> Told(IReadOnlyList<NoticeKind> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return [.. events.Select(NoticeKinds.Named).Distinct().Order()];
    }

    private static IReadOnlyList<NotificationDestinationId> SentTo(IReadOnlyList<NotificationDestinationId> destinations)
    {
        ArgumentNullException.ThrowIfNull(destinations);

        if (destinations.Count is 0 || destinations.Any(destination => destination is null))
        {
            throw new ArgumentException("A set of rules sends to at least one destination.", nameof(destinations));
        }

        return [.. destinations.Distinct()];
    }
}
