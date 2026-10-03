using System.Buffers.Binary;

namespace Carina.Infrastructure.Encodings;

/// <summary>
/// Turns off the flag that says a subtitle track of an MP4 file is enabled, so that a player shows the
/// track only when somebody chooses it. Only the three flag bytes of each subtitle track's header are
/// written; every other byte of the file, and every other track, is left as it is.
/// </summary>
public static class Mp4TrackFlags
{
    public const string SubtitleHandler = "sbtl";

    private const int Enabled = 0x01;

    private const int HeaderLength = 8;

    private const int LargeHeaderLength = 16;

    private const int HandlerAt = 8;

    /// <summary>
    /// The number of subtitle tracks whose flag was turned off.
    /// </summary>
    public static int TurnOffSubtitles(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        using FileStream file = new(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        return Children(file, 0, file.Length)
            .Where(box => box.Type is "moov")
            .SelectMany(movie => Children(file, movie.Body, movie.End))
            .Where(box => box.Type is "trak")
            .ToList()
            .Count(track => TurnedOff(file, track));
    }

    private static bool TurnedOff(FileStream file, Mp4Box track)
    {
        List<Mp4Box> parts = [.. Children(file, track.Body, track.End)];
        Mp4Box? header = parts.FirstOrDefault(part => part.Type is "tkhd");
        Mp4Box? media = parts.FirstOrDefault(part => part.Type is "mdia");

        if (header is null || media is null || Handler(file, media) is not SubtitleHandler || header.End - header.Body < 4)
        {
            return false;
        }

        Span<byte> flags = stackalloc byte[4];
        file.Position = header.Body;
        file.ReadExactly(flags);
        flags[3] = (byte)(flags[3] & ~Enabled);
        file.Position = header.Body;
        file.Write(flags);

        return true;
    }

    private static string? Handler(FileStream file, Mp4Box media)
    {
        Mp4Box? handler = Children(file, media.Body, media.End).FirstOrDefault(part => part.Type is "hdlr");

        if (handler is null || handler.End - handler.Body < HandlerAt + 4)
        {
            return null;
        }

        Span<byte> type = stackalloc byte[4];
        file.Position = handler.Body + HandlerAt;
        file.ReadExactly(type);

        return System.Text.Encoding.ASCII.GetString(type);
    }

    private static IEnumerable<Mp4Box> Children(FileStream file, long from, long end)
    {
        long at = from;
        byte[] header = new byte[LargeHeaderLength];

        while (end - at >= HeaderLength)
        {
            file.Position = at;
            file.ReadExactly(header, 0, HeaderLength);

            long size = BinaryPrimitives.ReadUInt32BigEndian(header);
            string type = System.Text.Encoding.ASCII.GetString(header, 4, 4);
            int headerLength = HeaderLength;

            if (size is 1)
            {
                if (end - at < LargeHeaderLength)
                {
                    yield break;
                }

                file.ReadExactly(header, HeaderLength, 8);
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(header.AsSpan(HeaderLength));
                headerLength = LargeHeaderLength;
            }
            else if (size is 0)
            {
                size = end - at;
            }

            if (size < headerLength || size > end - at)
            {
                yield break;
            }

            yield return new Mp4Box(type, at + headerLength, at + size);

            at += size;
        }
    }

    private sealed record Mp4Box(string Type, long Body, long End);
}
