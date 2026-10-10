using System.Buffers.Binary;
using Carina.Broadcast.Tables;

namespace Carina.Broadcast.DsmCc;

internal static class DsmCcMessage
{
    public const int ProtocolDiscriminator = 0x11;

    public const int DownloadType = 0x03;

    public const int HeaderSize = 12;

    public static bool TryRead(
        ReadOnlyMemory<byte> body,
        int messageId,
        out uint identifier,
        out ReadOnlyMemory<byte> message,
        out TableDefect defect)
    {
        identifier = 0;
        message = ReadOnlyMemory<byte>.Empty;

        if (body.Length < HeaderSize)
        {
            defect = TableDefect.SectionTooShort;

            return false;
        }

        ReadOnlySpan<byte> span = body.Span;

        if (span[0] != ProtocolDiscriminator || span[1] != DownloadType || BinaryPrimitives.ReadUInt16BigEndian(span[2..]) != messageId)
        {
            defect = TableDefect.UnexpectedMessage;

            return false;
        }

        int adaptationLength = span[9];
        int messageLength = BinaryPrimitives.ReadUInt16BigEndian(span[10..]);

        if (HeaderSize + messageLength > body.Length || adaptationLength > messageLength)
        {
            defect = TableDefect.LoopOverrun;

            return false;
        }

        identifier = BinaryPrimitives.ReadUInt32BigEndian(span[4..]);
        message = body.Slice(HeaderSize + adaptationLength, messageLength - adaptationLength);
        defect = default;

        return true;
    }
}
