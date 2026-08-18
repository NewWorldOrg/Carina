using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class ProgrammeId : IEquatable<ProgrammeId>
{
    public ProgrammeId(NetworkId networkId, ServiceId serviceId, EventId eventId)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(eventId);

        NetworkId = networkId;
        ServiceId = serviceId;
        EventId = eventId;
    }

    public NetworkId NetworkId { get; }

    public ServiceId ServiceId { get; }

    public EventId EventId { get; }

    public bool Equals(ProgrammeId? other)
        => other is not null
           && NetworkId.Equals(other.NetworkId)
           && ServiceId.Equals(other.ServiceId)
           && EventId.Equals(other.EventId);

    public override bool Equals(object? other) => Equals(other as ProgrammeId);

    public override int GetHashCode() => HashCode.Combine(NetworkId, ServiceId, EventId);

    public override string ToString() => $"{NetworkId.Value}-{ServiceId.Value}-{EventId.Value}";
}
