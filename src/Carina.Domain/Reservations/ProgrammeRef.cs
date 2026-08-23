using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;

namespace Carina.Domain.Reservations;

public sealed class ProgrammeRef : IEquatable<ProgrammeRef>
{
    public ProgrammeRef(NetworkId networkId, ServiceId serviceId, EventId eventId, DateTime startsAt)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(eventId);

        NetworkId = networkId;
        ServiceId = serviceId;
        EventId = eventId;
        StartsAt = UtcTimes.Required(startsAt, nameof(startsAt));
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public EventId EventId { get; }

    public DateTime StartsAt { get; }

    public ProgrammeId Id => new(NetworkId, ServiceId, EventId);

    public bool Equals(ProgrammeRef? other)
        => other is not null
           && NetworkId.Equals(other.NetworkId)
           && ServiceId.Equals(other.ServiceId)
           && EventId.Equals(other.EventId)
           && StartsAt == other.StartsAt;

    public override bool Equals(object? other) => Equals(other as ProgrammeRef);

    public override int GetHashCode() => HashCode.Combine(NetworkId, ServiceId, EventId, StartsAt);

    public override string ToString()
        => $"{NetworkId.Value}-{ServiceId.Value}-{EventId.Value}@{StartsAt:O}";
}
