using System.Buffers.Binary;
using System.Text;

namespace Carina.Infrastructure.Tests.Segments;

/// <summary>
/// Writes the elements of a Matroska stream byte by byte, for the reader to be handed shapes
/// ffmpeg writes and shapes it does not.
/// </summary>
internal static class MatroskaBytes
{
    public const uint EbmlHeader = 0x1A45DFA3;

    public const uint DocType = 0x4282;

    public const uint Segment = 0x18538067;

    public const uint SeekHead = 0x114D9B74;

    public const uint Info = 0x1549A966;

    public const uint TimestampScale = 0x2AD7B1;

    public const uint Tracks = 0x1654AE6B;

    public const uint TrackEntry = 0xAE;

    public const uint TrackNumber = 0xD7;

    public const uint TrackType = 0x83;

    public const uint CodecId = 0x86;

    public const uint DefaultDuration = 0x23E383;

    public const uint Video = 0xE0;

    public const uint PixelWidth = 0xB0;

    public const uint PixelHeight = 0xBA;

    public const uint Audio = 0xE1;

    public const uint SamplingFrequency = 0xB5;

    public const uint Channels = 0x9F;

    public const uint BitDepth = 0x6264;

    public const uint Tags = 0x1254C367;

    public const uint Cluster = 0x1F43B675;

    public const uint ClusterTimestamp = 0xE7;

    public const uint SimpleBlock = 0xA3;

    public const uint BlockGroup = 0xA0;

    public const uint Block = 0xA1;

    public const uint Cues = 0x1C53BB6B;

    public const uint Padding = 0xEC;

    public const uint Crc = 0xBF;

    public const byte KeyFrame = 0x80;

    public static byte[] Element(uint id, params byte[][] children)
    {
        byte[] body = [.. children.SelectMany(child => child)];

        return [.. Id(id), .. Size(body.Length), .. body];
    }

    public static byte[] Unsized(uint id) => [.. Id(id), 0x01, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF];

    public static byte[] Number(uint id, ulong value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, value);

        return Element(id, [.. bytes.SkipWhile(part => part is 0).DefaultIfEmpty((byte)0)]);
    }

    public static byte[] Text(uint id, string value) => Element(id, Encoding.ASCII.GetBytes(value));

    public static byte[] Float(uint id, double value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(bytes, value);

        return Element(id, bytes);
    }

    public static byte[] Filler(uint id, int length) => Element(id, new byte[length]);

    public static byte[] Blocked(uint id, int track, short relative, byte[] payload, byte flags = KeyFrame)
    {
        byte[] head = new byte[4];
        head[0] = (byte)(0x80 | track);
        BinaryPrimitives.WriteInt16BigEndian(head.AsSpan(1), relative);
        head[3] = flags;

        return Element(id, head, payload);
    }

    public static byte[] Header() => Element(EbmlHeader, Text(DocType, "matroska"));

    public static byte[] FrameTrack(int number, int width, int height, long nanosecondsAFrame)
        => Element(
            TrackEntry,
            Number(TrackNumber, (ulong)number),
            Number(TrackType, 1),
            Text(CodecId, "V_UNCOMPRESSED"),
            Number(DefaultDuration, (ulong)nanosecondsAFrame),
            Element(Video, Number(PixelWidth, (ulong)width), Number(PixelHeight, (ulong)height)));

    public static byte[] SoundTrack(int number)
        => Element(
            TrackEntry,
            Number(TrackNumber, (ulong)number),
            Number(TrackType, 2),
            Text(CodecId, "A_PCM/INT/LIT"),
            Element(Audio, Float(SamplingFrequency, 8000), Number(Channels, 2), Number(BitDepth, 16)));

    private static byte[] Id(uint id)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, id);

        return [.. bytes.SkipWhile(part => part is 0)];
    }

    private static byte[] Size(long size)
    {
        int width = 1;

        while (size >= (1L << (7 * width)) - 1)
        {
            width++;
        }

        byte[] bytes = new byte[width];
        long left = size;

        for (int at = width - 1; at >= 0; at--)
        {
            bytes[at] = (byte)(left & 0xFF);
            left >>= 8;
        }

        bytes[0] |= (byte)(0x80 >> (width - 1));

        return bytes;
    }
}
