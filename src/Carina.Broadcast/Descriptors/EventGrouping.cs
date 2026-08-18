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

public sealed record GroupedEvent(int ServiceId, int EventId);

public sealed record GroupedEventElsewhere(int NetworkId, int TransportStreamId, int ServiceId, int EventId);

public sealed class EventGrouping
{
    private const int HeaderSize = 1;

    private const int HereSize = 4;

    private const int ElsewhereSize = 8;

    private EventGrouping(
        EventGroupKind kind,
        IReadOnlyList<GroupedEvent> events,
        IReadOnlyList<GroupedEventElsewhere> elsewhere)
    {
        Kind = kind;
        Events = events;
        Elsewhere = elsewhere;
    }

    public EventGroupKind Kind { get; }

    public IReadOnlyList<GroupedEvent> Events { get; }

    public IReadOnlyList<GroupedEventElsewhere> Elsewhere { get; }

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
        var afterHere = HeaderSize + (count * HereSize);

        if (afterHere > payload.Length)
        {
            return false;
        }

        var events = new List<GroupedEvent>(count);

        for (var at = HeaderSize; at < afterHere; at += HereSize)
        {
            events.Add(new GroupedEvent(
                (payload[at] << 8) | payload[at + 1],
                (payload[at + 2] << 8) | payload[at + 3]));
        }

        if (kind is not (EventGroupKind.RelayedFromAnotherNetwork or EventGroupKind.MovedToAnotherNetwork))
        {
            grouping = new EventGrouping(kind, events, []);

            return true;
        }

        var left = payload.Length - afterHere;

        if (left % ElsewhereSize != 0)
        {
            return false;
        }

        var elsewhere = new List<GroupedEventElsewhere>(left / ElsewhereSize);

        for (var at = afterHere; at < payload.Length; at += ElsewhereSize)
        {
            elsewhere.Add(new GroupedEventElsewhere(
                (payload[at] << 8) | payload[at + 1],
                (payload[at + 2] << 8) | payload[at + 3],
                (payload[at + 4] << 8) | payload[at + 5],
                (payload[at + 6] << 8) | payload[at + 7]));
        }

        grouping = new EventGrouping(kind, events, elsewhere);

        return true;
    }

    private static EventGroupKind Kinds(int declared)
        => declared is >= (int)EventGroupKind.Shared and <= (int)EventGroupKind.MovedToAnotherNetwork
            ? (EventGroupKind)declared
            : EventGroupKind.Undefined;
}
