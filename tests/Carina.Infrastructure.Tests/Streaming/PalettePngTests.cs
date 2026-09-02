using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

using Carina.Infrastructure.Streaming;

namespace Carina.Infrastructure.Tests.Streaming;

public sealed class PalettePngTests
{
    private static readonly byte[] White = [0xff, 0xff, 0xff, 0xff];

    private static readonly byte[] HalfBlack = [0x00, 0x00, 0x00, 0x80];

    private static readonly byte[] Clear = [0x00, 0x00, 0x00, 0x00];

    private static readonly byte[] Orange = [0x00, 0x80, 0xff, 0xff];

    [Fact]
    public void ThePictureIsAPngWhoseColourTypeIsThePaletteOne()
    {
        byte[] png = PalettePng.Encode(Pixels(White, HalfBlack, Clear, Orange), 2, 2, 8);

        Assert.Equal([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a], png[..8]);

        Decoded read = Decoded.Of(png);

        Assert.Equal(2, read.Width);
        Assert.Equal(2, read.Height);
        Assert.Equal(8, read.BitDepth);
        Assert.Equal(3, read.ColourType);
        Assert.Equal(["IHDR", "PLTE", "tRNS", "IDAT", "IEND"], read.Chunks);
    }

    [Fact]
    public void EveryColourOfAPictureWithFewEnoughComesBackExactlyThroughThePaletteAndItsTransparency()
    {
        byte[] png = PalettePng.Encode(Pixels(White, HalfBlack, Clear, Orange), 2, 2, 8);

        Decoded read = Decoded.Of(png);

        Assert.Equal(4, read.Palette.Count);
        Assert.Equal([White, HalfBlack, Clear, Orange], read.Pixels().Select(Rgba).ToArray());
    }

    [Fact]
    public void FullyTransparentPixelsAreOneColourWhateverTheyWereBefore()
    {
        byte[] png = PalettePng.Encode(Pixels([0x12, 0x34, 0x56, 0x00], [0xff, 0xff, 0xff, 0x00]), 2, 1, 8);

        Decoded read = Decoded.Of(png);

        Assert.Single(read.Palette);
        Assert.All(read.Pixels(), pixel => Assert.Equal(0, pixel.Alpha));
    }

    [Fact]
    public void APictureOfMoreColoursThanAPaletteHoldsIsCoarsenedUntilItFits()
    {
        byte[] bgra = new byte[512 * 4];

        for (int at = 0; at < 512; at++)
        {
            bgra[at * 4] = (byte)(at & 0xff);
            bgra[(at * 4) + 1] = (byte)(at >> 1);
            bgra[(at * 4) + 2] = (byte)(255 - (at & 0xff));
            bgra[(at * 4) + 3] = 0xff;
        }

        Decoded read = Decoded.Of(PalettePng.Encode(bgra, 512, 1, 512 * 4));

        Assert.InRange(read.Palette.Count, 2, 256);

        Decoded.Pixel[] pixels = read.Pixels().ToArray();

        for (int at = 0; at < 512; at++)
        {
            Assert.InRange(Math.Abs(pixels[at].Red - (255 - (at & 0xff))), 0, 7);
            Assert.InRange(Math.Abs(pixels[at].Blue - (at & 0xff)), 0, 7);
            Assert.Equal(0xff, pixels[at].Alpha);
        }
    }

    [Fact]
    public void AStrideWiderThanTheRowReadsOnlyTheRow()
    {
        byte[] bgra = [.. White, .. Orange, .. HalfBlack, .. Clear, .. Orange, .. White];

        Decoded read = Decoded.Of(PalettePng.Encode(bgra, 2, 2, 12));

        Assert.Equal([White, Orange, Clear, Orange], read.Pixels().Select(Rgba).ToArray());
    }

    [Theory]
    [InlineData(0, 1, 4)]
    [InlineData(1, 0, 4)]
    [InlineData(2, 1, 4)]
    public void APictureWithNoPixelsOrAStrideNarrowerThanItsRowIsRefused(int width, int height, int stride)
    {
        Assert.ThrowsAny<ArgumentException>(() => PalettePng.Encode(new byte[16], width, height, stride));
    }

    [Fact]
    public void APictureShorterThanItsMeasurementsSayIsRefused()
    {
        Assert.Throws<ArgumentException>(() => PalettePng.Encode(new byte[15], 2, 2, 8));
    }

    private static byte[] Pixels(params byte[][] bgra) => [.. bgra.SelectMany(pixel => pixel)];

    private static byte[] Rgba(Decoded.Pixel pixel) => [pixel.Blue, pixel.Green, pixel.Red, pixel.Alpha];

    public sealed class Decoded
    {
        private Decoded(int width, int height, int bitDepth, int colourType, List<string> chunks, List<byte[]> palette, byte[] alphas, byte[] indices)
        {
            Width = width;
            Height = height;
            BitDepth = bitDepth;
            ColourType = colourType;
            Chunks = chunks;
            Palette = palette;
            Alphas = alphas;
            Indices = indices;
        }

        public int Width { get; }

        public int Height { get; }

        public int BitDepth { get; }

        public int ColourType { get; }

        public IReadOnlyList<string> Chunks { get; }

        public IReadOnlyList<byte[]> Palette { get; }

        public byte[] Alphas { get; }

        public byte[] Indices { get; }

        public static Decoded Of(byte[] png)
        {
            int at = 8;
            int width = 0;
            int height = 0;
            int bitDepth = 0;
            int colourType = 0;
            List<string> chunks = [];
            List<byte[]> palette = [];
            byte[] alphas = [];
            using MemoryStream compressed = new();

            while (at < png.Length)
            {
                int length = (int)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(at));
                string kind = Encoding.ASCII.GetString(png, at + 4, 4);
                ReadOnlySpan<byte> body = png.AsSpan(at + 8, length);

                chunks.Add(kind);

                switch (kind)
                {
                    case "IHDR":
                        width = (int)BinaryPrimitives.ReadUInt32BigEndian(body);
                        height = (int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]);
                        bitDepth = body[8];
                        colourType = body[9];
                        break;
                    case "PLTE":
                        for (int entry = 0; entry < length / 3; entry++)
                        {
                            palette.Add(body.Slice(entry * 3, 3).ToArray());
                        }

                        break;
                    case "tRNS":
                        alphas = body.ToArray();
                        break;
                    case "IDAT":
                        compressed.Write(body);
                        break;
                    default:
                        break;
                }

                at += 12 + length;
            }

            compressed.Position = 0;

            using ZLibStream inflating = new(compressed, CompressionMode.Decompress);
            using MemoryStream raw = new();

            inflating.CopyTo(raw);

            byte[] rows = raw.ToArray();
            byte[] indices = new byte[width * height];

            for (int row = 0; row < height; row++)
            {
                Assert.Equal(0, rows[row * (width + 1)]);
                Array.Copy(rows, (row * (width + 1)) + 1, indices, row * width, width);
            }

            return new Decoded(width, height, bitDepth, colourType, chunks, palette, alphas, indices);
        }

        public IEnumerable<Pixel> Pixels()
            => Indices.Select(index => new Pixel(
                Palette[index][0],
                Palette[index][1],
                Palette[index][2],
                index < Alphas.Length ? Alphas[index] : (byte)0xff));

        public readonly record struct Pixel(byte Red, byte Green, byte Blue, byte Alpha);
    }
}
