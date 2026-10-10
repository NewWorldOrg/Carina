namespace Carina.Broadcast.DsmCc;

public sealed record TimedEventMessage(
    int EventMessageGroupId,
    int EventMessageId,
    int EventMessageType,
    EventTimeMode TimeMode,
    long FiresAt,
    ReadOnlyMemory<byte> PrivateData)
{
    public bool IsImmediate => TimeMode == EventTimeMode.Immediate;
}
