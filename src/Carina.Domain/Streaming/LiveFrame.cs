using System.Buffers.Binary;

namespace Carina.Domain.Streaming;

public sealed record LiveFrame
{
    public const int HeaderLength = 9;

    public LiveFrame(LiveChannel channel, LivePts pts, ReadOnlyMemory<byte> payload)
    {
        if (!Enum.IsDefined(channel))
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel),
                channel,
                "A frame rides one of the channels the wire set aside.");
        }

        ArgumentNullException.ThrowIfNull(pts);

        Channel = channel;
        Pts = pts;
        Payload = payload;
    }

    public LiveChannel Channel { get; }

    public LivePts Pts { get; }

    public ReadOnlyMemory<byte> Payload { get; }

    public int Length => HeaderLength + Payload.Length;

    public static LiveFraming Read(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderLength)
        {
            return LiveFraming.Broken(LiveFrameFault.ShorterThanAHeader);
        }

        var channel = (LiveChannel)bytes[0];

        if (!Enum.IsDefined(channel))
        {
            return LiveFraming.Broken(LiveFrameFault.AChannelNobodySetAside);
        }

        return LiveFraming.Read(new LiveFrame(
            channel,
            LivePts.Of(BinaryPrimitives.ReadUInt64BigEndian(bytes[1..HeaderLength])),
            bytes[HeaderLength..].ToArray()));
    }

    public void WriteTo(Span<byte> destination)
    {
        if (destination.Length < Length)
        {
            throw new ArgumentException(
                "A frame is written whole or not at all, and this is shorter than the frame.",
                nameof(destination));
        }

        destination[0] = (byte)Channel;
        BinaryPrimitives.WriteUInt64BigEndian(destination[1..HeaderLength], Pts.Value);
        Payload.Span.CopyTo(destination[HeaderLength..]);
    }

    public byte[] ToArray()
    {
        byte[] written = new byte[Length];

        WriteTo(written);

        return written;
    }
}
