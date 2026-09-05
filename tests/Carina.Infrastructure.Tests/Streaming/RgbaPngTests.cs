using System.Buffers.Binary;
using System.IO.Compression;

using Carina.Domain.Streaming;
using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class RgbaPngTests
{
    private static readonly VideoSize TwoByTwo = new(2, 2);

    private static readonly byte[] Rgba =
    [
        0x10, 0x20, 0x30, 0xff, 0x40, 0x50, 0x60, 0x80,
        0x00, 0x00, 0x00, 0x00, 0xff, 0xee, 0xdd, 0x01,
    ];

    private static readonly byte[] Bgra =
    [
        0x30, 0x20, 0x10, 0xff, 0x60, 0x50, 0x40, 0x80,
        0x00, 0x00, 0x00, 0x00, 0xdd, 0xee, 0xff, 0x01,
    ];

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void EveryRowFilterDecodesToTheSamePixelsInTheCanvasByteOrder(int filter)
    {
        byte[] decoded = new byte[Bgra.Length];

        Assert.True(RgbaPng.TryDecode(Encoded(Rgba, TwoByTwo, (byte)filter), TwoByTwo, decoded));
        Assert.Equal(Bgra, decoded);
    }

    [Fact]
    public void ThePixelsMayArriveInSeveralDataChunks()
    {
        byte[] decoded = new byte[Bgra.Length];

        Assert.True(RgbaPng.TryDecode(Encoded(Rgba, TwoByTwo, 0, idatChunks: 3), TwoByTwo, decoded));
        Assert.Equal(Bgra, decoded);
    }

    [Fact]
    public void APictureOfAnotherSizeThanTheCanvasIsRefused()
    {
        Assert.False(RgbaPng.TryDecode(Encoded(Rgba, TwoByTwo, 0), new VideoSize(4, 1), new byte[16]));
        Assert.False(RgbaPng.TryDecode(Encoded(Rgba, TwoByTwo, 0), new VideoSize(2, 2), new byte[15]));
    }

    [Fact]
    public void APalettePictureIsNotWhatTheDecoderDrawsAndIsRefused()
    {
        byte[] palette = PalettePng.Encode(Bgra, 2, 2, 8);

        Assert.False(RgbaPng.TryDecode(palette, TwoByTwo, new byte[16]));
    }

    [Fact]
    public void BytesThatAreNotAPngAreRefused()
    {
        Assert.False(RgbaPng.TryDecode([1, 2, 3], TwoByTwo, new byte[16]));
        Assert.False(RgbaPng.TryDecode(Encoded(Rgba, TwoByTwo, 0)[..20], TwoByTwo, new byte[16]));
    }

    [Fact]
    public void AnUnknownRowFilterIsRefusedRatherThanGuessed()
    {
        Assert.False(RgbaPng.TryDecode(Encoded(Rgba, TwoByTwo, 7), TwoByTwo, new byte[16]));
    }

    [Fact]
    public void APictureWhoseRowsRunOutIsRefused()
    {
        Assert.False(RgbaPng.TryDecode(Encoded(Rgba[..8], new VideoSize(2, 1), 0, claimedHeight: 2), TwoByTwo, new byte[16]));
    }

    public static byte[] Encoded(byte[] rgba, VideoSize size, byte filter, int idatChunks = 1, int? claimedHeight = null)
    {
        int stride = size.Width * 4;
        byte[] raw = new byte[(stride + 1) * size.Height];

        for (int row = 0; row < size.Height; row++)
        {
            raw[row * (stride + 1)] = filter;
            Filtered(rgba.AsSpan(row * stride, stride), row is 0 ? new byte[stride] : rgba.AsSpan((row - 1) * stride, stride), filter)
                .CopyTo(raw.AsSpan((row * (stride + 1)) + 1));
        }

        using MemoryStream compressed = new();

        using (ZLibStream zlib = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        byte[] deflated = compressed.ToArray();
        using MemoryStream png = new();

        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);

        byte[] header = new byte[13];

        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)size.Width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)(claimedHeight ?? size.Height));
        header[8] = 8;
        header[9] = 6;
        Chunk(png, "IHDR"u8, header);

        int each = Math.Max(1, deflated.Length / idatChunks);

        for (int at = 0; at < deflated.Length; at += each)
        {
            Chunk(png, "IDAT"u8, deflated.AsSpan(at, Math.Min(each, deflated.Length - at)));
        }

        Chunk(png, "IEND"u8, []);

        return png.ToArray();
    }

    private static byte[] Filtered(ReadOnlySpan<byte> line, ReadOnlySpan<byte> above, byte filter)
    {
        byte[] filtered = new byte[line.Length];

        for (int at = 0; at < line.Length; at++)
        {
            int left = at < 4 ? 0 : line[at - 4];
            int upperLeft = at < 4 ? 0 : above[at - 4];
            int predicted = filter switch
            {
                1 => left,
                2 => above[at],
                3 => (left + above[at]) >> 1,
                4 => Paeth(left, above[at], upperLeft),
                _ => 0,
            };

            filtered[at] = (byte)(line[at] - predicted);
        }

        return filtered;
    }

    private static int Paeth(int left, int above, int upperLeft)
    {
        int estimate = left + above - upperLeft;
        int toLeft = Math.Abs(estimate - left);
        int toAbove = Math.Abs(estimate - above);
        int toUpperLeft = Math.Abs(estimate - upperLeft);

        if (toLeft <= toAbove && toLeft <= toUpperLeft)
        {
            return left;
        }

        return toAbove <= toUpperLeft ? above : upperLeft;
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> body)
    {
        Span<byte> length = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        png.Write(length);
        png.Write(kind);
        png.Write(body);
        png.Write(new byte[4]);
    }
}
