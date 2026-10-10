namespace Carina.Broadcast.DsmCc;

public sealed class TimedEventMessage
{
    internal TimedEventMessage(
        int eventMessageGroupId,
        int eventMessageId,
        int eventMessageType,
        EventTimeMode timeMode,
        long firesAt,
        ReadOnlyMemory<byte> privateData)
    {
        EventMessageGroupId = eventMessageGroupId;
        EventMessageId = eventMessageId;
        EventMessageType = eventMessageType;
        TimeMode = timeMode;
        FiresAt = firesAt;
        PrivateData = privateData;
    }

    public int EventMessageGroupId { get; }

    public int EventMessageId { get; }

    public int EventMessageType { get; }

    public EventTimeMode TimeMode { get; }

    public bool IsImmediate => TimeMode == EventTimeMode.Immediate;

    public long FiresAt { get; }

    public ReadOnlyMemory<byte> PrivateData { get; }
}
