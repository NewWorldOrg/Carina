using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Carina.Broadcast.DsmCc;

internal static class ModuleInflater
{
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
            return CarouselDefect.InflatedSizeMismatch;
        }

        Span<byte> beyond = stackalloc byte[1];

        return zlib.Read(beyond) > 0 ? CarouselDefect.OriginalSizeExceeded : default;
    }
}
