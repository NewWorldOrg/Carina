namespace Carina.Domain.DataBroadcast;

/// <summary>
/// One event message of a data broadcast, with the moment it fires on the <see cref="StreamClock"/>, never a
/// raw PTS.
/// </summary>
public sealed record EventMessage
{
    public EventMessage(int group, int id, int messageType, EventTiming timing, long firesAt, ReadOnlyMemory<byte> privateData)
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
    }

    public int Group { get; }

    public int Id { get; }

    public int MessageType { get; }

    public EventTiming Timing { get; }

    public bool IsImmediate => Timing == EventTiming.Immediate;

    public long FiresAt { get; }

    public TimeSpan At => StreamClock.ToTime(FiresAt);

    public ReadOnlyMemory<byte> PrivateData { get; }
}
