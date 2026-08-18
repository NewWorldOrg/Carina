using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Descriptors;

public enum EventGroupKind
{
    Undefined = 0,

    Shared = 1,

    Relayed = 2,

    Moved = 3,

    RelayedFromAnotherNetwork = 4,

    MovedToAnotherNetwork = 5,
}

public sealed record GroupedEvent(int NetworkId, int TransportStreamId, int ServiceId, int EventId);

public sealed record EventGrouping(EventGroupKind Kind, IReadOnlyList<GroupedEvent> Events)
{
    private const int HeaderSize = 1;

    private const int SameNetworkSize = 4;

    private const int OtherNetworkSize = 8;

    public static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out EventGrouping? grouping)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        grouping = null;

        if (descriptor.Tag != DescriptorTags.EventGroup || descriptor.Payload.Length < HeaderSize)
        {
            return false;
        }

        var payload = descriptor.Payload.Span;
        var kind = Kinds(payload[0] >> 4);
        var count = payload[0] & 0x0F;
        var crosses = kind is EventGroupKind.RelayedFromAnotherNetwork or EventGroupKind.MovedToAnotherNetwork;
        var size = crosses ? OtherNetworkSize : SameNetworkSize;

        if (HeaderSize + (count * size) > payload.Length)
        {
            return false;
        }

        var events = new List<GroupedEvent>(count);
        var at = HeaderSize;

        for (var read = 0; read < count; read++)
        {
            events.Add(crosses
                ? new GroupedEvent(
                    (payload[at] << 8) | payload[at + 1],
                    (payload[at + 2] << 8) | payload[at + 3],
                    (payload[at + 4] << 8) | payload[at + 5],
                    (payload[at + 6] << 8) | payload[at + 7])
                : new GroupedEvent(
                    0,
                    0,
                    (payload[at] << 8) | payload[at + 1],
                    (payload[at + 2] << 8) | payload[at + 3]));

            at += size;
        }

        grouping = new EventGrouping(kind, events);

        return true;
    }

    private static EventGroupKind Kinds(int declared)
        => declared is >= (int)EventGroupKind.Shared and <= (int)EventGroupKind.MovedToAnotherNetwork
            ? (EventGroupKind)declared
            : EventGroupKind.Undefined;
}
