using System.Diagnostics.CodeAnalysis;

using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.DsmCc;

public sealed class GeneralEvent
{
    public const int Immediate = 0x00;

    public const int NptTime = 0x02;

    private const int FixedFieldsSize = 11;

    private GeneralEvent(
        int eventMessageGroupId,
        int timeMode,
        long? npt,
        int eventMessageType,
        int eventMessageId,
        ReadOnlyMemory<byte> privateData)
    {
        EventMessageGroupId = eventMessageGroupId;
        TimeMode = timeMode;
        Npt = npt;
        EventMessageType = eventMessageType;
        EventMessageId = eventMessageId;
        PrivateData = privateData;
    }

    public int EventMessageGroupId { get; }

    public int TimeMode { get; }

    public long? Npt { get; }

    public int EventMessageType { get; }

    public int EventMessageId { get; }

    public ReadOnlyMemory<byte> PrivateData { get; }

    internal static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out GeneralEvent? read)
    {
        read = null;

        if (descriptor.Payload.Length < FixedFieldsSize)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;
        int timeMode = payload[2];

        read = new GeneralEvent(
            (payload[0] << 4) | (payload[1] >> 4),
            timeMode,
            timeMode == NptTime ? ThirtyThreeBits(payload[3..8]) : null,
            payload[8],
            (payload[9] << 8) | payload[10],
            descriptor.Payload[FixedFieldsSize..]);

        return true;
    }

    internal static long ThirtyThreeBits(ReadOnlySpan<byte> fiveBytes)
        => ((long)(fiveBytes[0] & 0x01) << 32)
            | ((long)fiveBytes[1] << 24)
            | ((long)fiveBytes[2] << 16)
            | ((long)fiveBytes[3] << 8)
            | fiveBytes[4];
}
