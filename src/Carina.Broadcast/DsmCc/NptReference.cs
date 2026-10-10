using System.Diagnostics.CodeAnalysis;
using Carina.Broadcast.Descriptors;

namespace Carina.Broadcast.DsmCc;

public sealed record NptReference(
    bool PostDiscontinuity,
    int ContentId,
    long Stc,
    long Npt,
    int ScaleNumerator,
    int ScaleDenominator)
{
    private const int Size = 18;

    public bool IsUsable => ScaleNumerator != 0 && ScaleDenominator != 0;

    internal static bool TryRead(Descriptor descriptor, [NotNullWhen(true)] out NptReference? read)
    {
        read = null;

        if (descriptor.Payload.Length < Size)
        {
            return false;
        }

        ReadOnlySpan<byte> payload = descriptor.Payload.Span;

        read = new NptReference(
            (payload[0] & 0x80) != 0,
            payload[0] & 0x7F,
            GeneralEvent.ThirtyThreeBits(payload[1..6]),
            GeneralEvent.ThirtyThreeBits(payload[9..14]),
            (payload[14] << 8) | payload[15],
            (payload[16] << 8) | payload[17]);

        return true;
    }
}
