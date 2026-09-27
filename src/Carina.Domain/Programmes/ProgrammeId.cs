using Carina.Domain.Base;
using Carina.Domain.Channels;

namespace Carina.Domain.Programmes;

public sealed class ProgrammeId : CommonValueObject<(NetworkId NetworkId, ServiceId ServiceId, EventId EventId)>
{
    public ProgrammeId(NetworkId networkId, ServiceId serviceId, EventId eventId)
        : base(Validated(networkId, serviceId, eventId))
    {
    }

    public NetworkId NetworkId => Value.NetworkId;

    public ServiceId ServiceId => Value.ServiceId;

    public EventId EventId => Value.EventId;

    public override string ToString() => $"{NetworkId.Value}-{ServiceId.Value}-{EventId.Value}";

    private static (NetworkId, ServiceId, EventId) Validated(
        NetworkId networkId,
        ServiceId serviceId,
        EventId eventId)
    {
        ArgumentNullException.ThrowIfNull(networkId);
        ArgumentNullException.ThrowIfNull(serviceId);
        ArgumentNullException.ThrowIfNull(eventId);

        return (networkId, serviceId, eventId);
    }
}
