using System.Buffers.Binary;
using System.IO.Compression;

namespace Carina.Infrastructure.Streaming;

public static class PalettePng
{
    public const int BytesPerPixel = 4;

    public const int ColoursAtMost = 256;

    public const byte IndexedColour = 3;

    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(stride, width * BytesPerPixel);

        if (bgra.Length < (height - 1) * stride + width * BytesPerPixel)
        {
            throw new ArgumentException("The picture is shorter than its width, height and stride say.", nameof(bgra));
        }

        Quantised quantised = Quantise(bgra, width, height, stride);

        using MemoryStream png = new();

        png.Write(Signature);
        Chunk(png, "IHDR"u8, Header(width, height));
        Chunk(png, "PLTE"u8, quantised.Palette);
        Chunk(png, "tRNS"u8, quantised.Transparency);
        Chunk(png, "IDAT"u8, Deflated(quantised.Indices, width, height));
        Chunk(png, "IEND"u8, []);

        return png.ToArray();
    }

    private static Quantised Quantise(ReadOnlySpan<byte> bgra, int width, int height, int stride)
    {
        int shift = 0;
        Dictionary<uint, byte> palette = [];
        byte[] indices = new byte[width * height];

        while (!TryQuantise(bgra, width, height, stride, shift, palette, indices))
        {
            shift++;
            palette.Clear();
        }

        byte[] colours = new byte[palette.Count * 3];
        byte[] alphas = new byte[palette.Count];

        foreach ((uint colour, byte index) in palette)
        {
            colours[index * 3] = (byte)(colour >> 16);
            colours[(index * 3) + 1] = (byte)(colour >> 8);
            colours[(index * 3) + 2] = (byte)colour;
            alphas[index] = (byte)(colour >> 24);
        }

        return new Quantised(indices, colours, alphas);
    }

    private static bool TryQuantise(
        ReadOnlySpan<byte> bgra,
        int width,
        int height,
        int stride,
        int shift,
        Dictionary<uint, byte> palette,
        byte[] indices)
    {
        uint mask = 0xffu >> shift << shift;

        for (int row = 0; row < height; row++)
        {
            ReadOnlySpan<byte> line = bgra.Slice(row * stride, width * BytesPerPixel);

            for (int column = 0; column < width; column++)
            {
                int at = column * BytesPerPixel;
                uint colour = Colour(line[at + 3], line[at + 2] & mask, line[at + 1] & mask, line[at] & mask);

                if (!palette.TryGetValue(colour, out byte index))
                {
                    if (palette.Count == ColoursAtMost)
                    {
                        return false;
                    }

                    index = (byte)palette.Count;
                    palette[colour] = index;
                }

                indices[(row * width) + column] = index;
            }
        }

        return true;
    }

    private static uint Colour(uint alpha, uint red, uint green, uint blue)
        => alpha is 0 ? 0u : (alpha << 24) | (red << 16) | (green << 8) | blue;

    private static byte[] Header(int width, int height)
    {
        byte[] header = new byte[13];

        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(4), (uint)height);
        header[8] = 8;
        header[9] = IndexedColour;

        return header;
    }

    private static byte[] Deflated(byte[] indices, int width, int height)
    {
        using MemoryStream compressed = new();

        using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            byte[] filter = [0];

            for (int row = 0; row < height; row++)
            {
                zlib.Write(filter);
                zlib.Write(indices, row * width, width);
            }
        }

        return compressed.ToArray();
    }

    private static void Chunk(Stream png, ReadOnlySpan<byte> kind, ReadOnlySpan<byte> body)
    {
        Span<byte> length = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)body.Length);
        png.Write(length);
        png.Write(kind);
        png.Write(body);

        uint crc = Crc(Crc(0xffffffffu, kind), body) ^ 0xffffffffu;
        Span<byte> check = stackalloc byte[4];

        BinaryPrimitives.WriteUInt32BigEndian(check, crc);
        png.Write(check);
    }

    private static uint Crc(uint running, ReadOnlySpan<byte> bytes)
    {
        foreach (byte one in bytes)
        {
            running = CrcTable[(running ^ one) & 0xffu] ^ (running >> 8);
        }

        return running;
    }

    private static uint[] BuildCrcTable()
    {
        uint[] table = new uint[256];

        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;

            for (int k = 0; k < 8; k++)
            {
                c = (c & 1u) is 1u ? 0xedb88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }

    private readonly record struct Quantised(byte[] Indices, byte[] Palette, byte[] Transparency);
}
