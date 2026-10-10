using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Carina.Broadcast.DsmCc;

internal static class ModuleInflater
{
    private const int HeaderSize = 2;

    private const int ChecksumSize = 4;

    private const uint AdlerModulus = 65521;

    public static bool TryInflate(
        ReadOnlyMemory<byte> compressed,
        int originalSize,
        [NotNullWhen(true)] out byte[]? inflated,
        out CarouselDefect defect)
    {
        byte[] target = new byte[originalSize];
        inflated = null;

        try
        {
            defect = Fill(compressed, target);
        }
        catch (InvalidDataException)
        {
            defect = CarouselDefect.DecompressionFailed;
        }

        if (defect != default)
        {
            return false;
        }

        inflated = target;

        return true;
    }

    private static CarouselDefect Fill(ReadOnlyMemory<byte> compressed, byte[] target)
    {
        using MemoryStream source = new(compressed.ToArray(), writable: false);
        using ZLibStream zlib = new(source, CompressionMode.Decompress);
        int filled = 0;
        int read = -1;

        while (filled < target.Length && read != 0)
        {
            read = zlib.Read(target, filled, target.Length - filled);
            filled += read;
        }

        if (filled < target.Length)
        {
            return CarriesItsChecksum(compressed.Span, target.AsSpan(0, filled))
                ? CarouselDefect.InflatedSizeMismatch
                : CarouselDefect.DecompressionFailed;
        }

        Span<byte> beyond = stackalloc byte[1];

        if (zlib.Read(beyond) > 0)
        {
            return CarouselDefect.OriginalSizeExceeded;
        }

        return CarriesItsChecksum(compressed.Span, target) ? default : CarouselDefect.DecompressionFailed;
    }

    private static bool CarriesItsChecksum(ReadOnlySpan<byte> compressed, ReadOnlySpan<byte> inflated)
        => compressed.Length >= HeaderSize + ChecksumSize
            && BinaryPrimitives.ReadUInt32BigEndian(compressed[^ChecksumSize..]) == Adler32(inflated);

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint sum = 1;
        uint ofSums = 0;

        foreach (byte octet in data)
        {
            sum = (sum + octet) % AdlerModulus;
            ofSums = (ofSums + sum) % AdlerModulus;
        }

        return (ofSums << 16) | sum;
    }
}
