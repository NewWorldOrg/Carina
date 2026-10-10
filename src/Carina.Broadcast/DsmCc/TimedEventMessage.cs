namespace Carina.Broadcast.DsmCc;

public sealed record TimedEventMessage(
    int EventMessageGroupId,
    int EventMessageId,
    int EventMessageType,
    int TimeMode,
    long FiresAt,
    ReadOnlyMemory<byte> PrivateData);
