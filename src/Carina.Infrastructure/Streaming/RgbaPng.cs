using System.Buffers.Binary;
using System.IO.Compression;

using Carina.Domain.Streaming;

namespace Carina.Infrastructure.Streaming;

public static class RgbaPng
{
    public const byte TruecolourWithAlpha = 6;

    private const int BytesPerPixel = 4;

    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryDecode(ReadOnlySpan<byte> png, VideoSize size, Span<byte> bgra)
    {
        ArgumentNullException.ThrowIfNull(size);

        int stride = size.Width * BytesPerPixel;

        if (bgra.Length < stride * size.Height || png.Length < Signature.Length || !png[..Signature.Length].SequenceEqual(Signature))
        {
            return false;
        }

        using MemoryStream compressed = new();
        bool headed = false;
        int at = Signature.Length;

        while (at + 8 <= png.Length)
        {
            int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png[at..]);
            ReadOnlySpan<byte> kind = png.Slice(at + 4, 4);

            if (length < 0 || at + 12 + length > png.Length)
            {
                return false;
            }

            ReadOnlySpan<byte> body = png.Slice(at + 8, length);

            if (kind.SequenceEqual("IHDR"u8))
            {
                if (!Fits(body, size))
                {
                    return false;
                }

                headed = true;
            }
            else if (kind.SequenceEqual("IDAT"u8))
            {
                compressed.Write(body);
            }
            else if (kind.SequenceEqual("IEND"u8))
            {
                break;
            }

            at += 12 + length;
        }

        if (!headed || compressed.Length is 0)
        {
            return false;
        }

        compressed.Position = 0;

        return Unfiltered(compressed, size, stride, bgra);
    }

    private static bool Fits(ReadOnlySpan<byte> header, VideoSize size)
        => header.Length is 13
           && BinaryPrimitives.ReadUInt32BigEndian(header) == (uint)size.Width
           && BinaryPrimitives.ReadUInt32BigEndian(header[4..]) == (uint)size.Height
           && header[8] is 8
           && header[9] == TruecolourWithAlpha
           && header[10] is 0
           && header[11] is 0
           && header[12] is 0;

    private static bool Unfiltered(Stream compressed, VideoSize size, int stride, Span<byte> bgra)
    {
        byte[] previous = new byte[stride];
        byte[] current = new byte[stride];
        byte[] filter = new byte[1];

        try
        {
            using ZLibStream inflating = new(compressed, CompressionMode.Decompress);

            for (int row = 0; row < size.Height; row++)
            {
                inflating.ReadExactly(filter);
                inflating.ReadExactly(current);

                if (!Unfilter(filter[0], current, previous))
                {
                    return false;
                }

                Span<byte> line = bgra.Slice(row * stride, stride);

                for (int at = 0; at < stride; at += BytesPerPixel)
                {
                    line[at] = current[at + 2];
                    line[at + 1] = current[at + 1];
                    line[at + 2] = current[at];
                    line[at + 3] = current[at + 3];
                }

                (previous, current) = (current, previous);
            }
        }
        catch (Exception broken) when (broken is InvalidDataException or EndOfStreamException)
        {
            return false;
        }

        return true;
    }

    private static bool Unfilter(byte filter, byte[] line, byte[] above)
    {
        switch (filter)
        {
            case 0:
                return true;
            case 1:
                for (int at = BytesPerPixel; at < line.Length; at++)
                {
                    line[at] += line[at - BytesPerPixel];
                }

                return true;
            case 2:
                for (int at = 0; at < line.Length; at++)
                {
                    line[at] += above[at];
                }

                return true;
            case 3:
                for (int at = 0; at < line.Length; at++)
                {
                    int left = at < BytesPerPixel ? 0 : line[at - BytesPerPixel];

                    line[at] += (byte)((left + above[at]) >> 1);
                }

                return true;
            case 4:
                for (int at = 0; at < line.Length; at++)
                {
                    int left = at < BytesPerPixel ? 0 : line[at - BytesPerPixel];
                    int upperLeft = at < BytesPerPixel ? 0 : above[at - BytesPerPixel];

                    line[at] += Paeth(left, above[at], upperLeft);
                }

                return true;
            default:
                return false;
        }
    }

    private static byte Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);

        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return (byte)left;
        }

        return toAbove <= toUpperLeft ? (byte)above : (byte)upperLeft;
    }
}
