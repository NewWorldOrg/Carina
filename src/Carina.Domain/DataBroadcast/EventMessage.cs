namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One event message of a data broadcast, with the moment it fires on the <see cref="StreamClock"/>, never a
/// raw PTS, and the tag of the stream it came on, unknown for one kept before that was kept.
/// </summary>
public sealed record EventMessage
{
    public const int FramingBytes = sizeof(byte) + sizeof(ushort) + sizeof(ushort) + sizeof(byte) + sizeof(byte) + sizeof(ulong) + sizeof(ushort);

    public const int ComponentTagBytes = sizeof(byte);

    public EventMessage(
        int group,
        int id,
        int messageType,
        EventTiming timing,
        long firesAt,
        ReadOnlyMemory<byte> privateData,
        int? componentTag = null)
    {
        if (!Enum.IsDefined(timing))
        {
            throw new ArgumentOutOfRangeException(nameof(timing), timing, "An event message fires at once or at a moment of the programme.");
        }

        Group = CarouselNumbers.Within(group, CarouselNumbers.MostEventGroup, nameof(group), "An event message group is twelve bits.");
        Id = CarouselNumbers.Within(id, CarouselNumbers.MostEventId, nameof(id), "An event message id is two bytes.");
        MessageType = CarouselNumbers.Within(messageType, CarouselNumbers.MostEventType, nameof(messageType), "An event message type is one byte.");
        Timing = timing;
        FiresAt = firesAt;
        PrivateData = privateData;
        ComponentTag = componentTag is { } tag ? CarouselNumbers.Tag(tag, nameof(componentTag)) : null;
    }

    public int Group { get; }

    public int Id { get; }

    public int MessageType { get; }

    public EventTiming Timing { get; }

    public bool IsImmediate => Timing == EventTiming.Immediate;

    public long FiresAt { get; }

    public TimeSpan At => StreamClock.ToTime(FiresAt);

    public ReadOnlyMemory<byte> PrivateData { get; }

    public int? ComponentTag { get; }

    /// <summary>
    /// The bytes it takes in a record: its kind, group, id, type, timing, moment and the length of its private
    /// data, the private data, and the tag of its stream when that is known.
    /// </summary>
    public long Bytes => FramingBytes + (long)PrivateData.Length + (ComponentTag is null ? 0 : ComponentTagBytes);

    public EventMessage Shifted(long by) => new(Group, Id, MessageType, Timing, FiresAt + by, PrivateData, ComponentTag);
}
