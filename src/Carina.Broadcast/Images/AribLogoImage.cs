using System.Diagnostics.CodeAnalysis;

namespace Carina.Broadcast.Images;

public sealed class AribLogoImage
{
    public const int SignatureSize = 8;

    public const int HeaderChunkSize = 25;

    public const int AfterHeader = SignatureSize + HeaderChunkSize;

    public const byte PaletteColourType = 3;

    public const byte EightBitsPerSample = 8;

    private const int HeaderDataSize = 13;

    private const int ChunkOverhead = 12;

    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly byte[] HeaderChunkType = "IHDR"u8.ToArray();

    private static readonly byte[] PaletteChunkType = "PLTE"u8.ToArray();

    private static readonly byte[] OpacityChunkType = "tRNS"u8.ToArray();

    private AribLogoImage(int width, int height, ReadOnlyMemory<byte> bytes)
    {
        Width = width;
        Height = height;
        Bytes = bytes;
    }

    public int Width { get; }

    public int Height { get; }

    public ReadOnlyMemory<byte> Bytes { get; }

    public static bool TryRead(ReadOnlyMemory<byte> broadcast, [NotNullWhen(true)] out AribLogoImage? image)
    {
        image = null;
        ReadOnlySpan<byte> png = broadcast.Span;

        if (png.Length < AfterHeader
            || !png[..SignatureSize].SequenceEqual(Signature)
            || WordAt(png, SignatureSize) != HeaderDataSize
            || !png.Slice(SignatureSize + 4, 4).SequenceEqual(HeaderChunkType)
            || Crc32Png.Compute(png.Slice(SignatureSize + 4, 4 + HeaderDataSize))
                != WordAt(png, AfterHeader - Crc32Png.ChecksumSize))
        {
            return false;
        }

        uint width = WordAt(png, SignatureSize + 8);
        uint height = WordAt(png, SignatureSize + 12);

        if (width is 0 or > int.MaxValue || height is 0 or > int.MaxValue)
        {
            return false;
        }

        image = new AribLogoImage(
            (int)width,
            (int)height,
            NeedsThePalette(png) ? WithThePalette(png) : broadcast);

        return true;
    }

    private static bool NeedsThePalette(ReadOnlySpan<byte> png)
        => png[SignatureSize + 16] == EightBitsPerSample
            && png[SignatureSize + 17] == PaletteColourType
            && !CarriesThePalette(png);

    private static bool CarriesThePalette(ReadOnlySpan<byte> png)
        => png.Length >= AfterHeader + 8
            && png.Slice(AfterHeader + 4, 4).SequenceEqual(PaletteChunkType);

    private static byte[] WithThePalette(ReadOnlySpan<byte> png)
    {
        byte[] palette = Chunk(PaletteChunkType, AribLogoPalette.Rgb.Span);
        byte[] opacity = Chunk(OpacityChunkType, AribLogoPalette.Opacities.Span);
        byte[] complete = new byte[png.Length + palette.Length + opacity.Length];

        png[..AfterHeader].CopyTo(complete);
        palette.CopyTo(complete.AsSpan(AfterHeader));
        opacity.CopyTo(complete.AsSpan(AfterHeader + palette.Length));
        png[AfterHeader..].CopyTo(complete.AsSpan(AfterHeader + palette.Length + opacity.Length));

        return complete;
    }

    private static byte[] Chunk(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        byte[] chunk = new byte[ChunkOverhead + data.Length];

        WriteWord(chunk.AsSpan(0), (uint)data.Length);
        type.CopyTo(chunk.AsSpan(4));
        data.CopyTo(chunk.AsSpan(8));
        WriteWord(chunk.AsSpan(8 + data.Length), Crc32Png.Compute(chunk.AsSpan(4, 4 + data.Length)));

        return chunk;
    }

    private static uint WordAt(ReadOnlySpan<byte> bytes, int at)
        => ((uint)bytes[at] << 24) | ((uint)bytes[at + 1] << 16) | ((uint)bytes[at + 2] << 8) | bytes[at + 3];

    private static void WriteWord(Span<byte> bytes, uint value)
    {
        bytes[0] = (byte)(value >> 24);
        bytes[1] = (byte)(value >> 16);
        bytes[2] = (byte)(value >> 8);
        bytes[3] = (byte)value;
    }
}
