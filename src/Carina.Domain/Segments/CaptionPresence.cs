using Carina.Domain.Captions;

namespace Carina.Domain.Segments;

/// <summary>
/// Whether captions are shown in each second of a recording's own time, read from the captions taken
/// from its file and placed the way they are placed over the recording played as it is. A second shows
/// captions when a picture is on screen at any moment of it; a picture stays until the next change, and
/// the last one to the end of what was read.
/// </summary>
public static class CaptionPresence
{
    public const byte Hidden = 0;

    public const byte Shown = 1;

    public const int PerChunk = LearningData.ChunkSeconds;

    /// <summary>
    /// The seconds of the first <paramref name="length"/> of the recording, a part per chunk from the first,
    /// a part second at the end counted as a second.
    /// </summary>
    public static IReadOnlyList<LearningDataPart> Parts(CaptionRecord record, TimeSpan length)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentOutOfRangeException.ThrowIfLessThan(length, TimeSpan.Zero);

        byte[] seconds = Seconds(CaptionWindow.Placed(record, record.StartsAt, length), length);

        return [.. seconds.Chunk(PerChunk).Select((chunk, index) => LearningDataPart.OfCaptions(index, chunk))];
    }

    private static byte[] Seconds(IReadOnlyList<PlacedCaption> placed, TimeSpan length)
    {
        byte[] seconds = new byte[SecondsUntil(length)];

        for (int at = 0; at < placed.Count; at++)
        {
            if (placed[at].Picture is null)
            {
                continue;
            }

            TimeSpan until = at + 1 < placed.Count ? placed[at + 1].At : length;

            Mark(seconds, placed[at].At, until);
        }

        return seconds;
    }

    private static void Mark(byte[] seconds, TimeSpan from, TimeSpan until)
    {
        if (until <= from)
        {
            return;
        }

        long end = Math.Min(seconds.Length, SecondsUntil(until));

        for (long second = from.Ticks / TimeSpan.TicksPerSecond; second < end; second++)
        {
            seconds[second] = Shown;
        }
    }

    private static int SecondsUntil(TimeSpan at) => (int)((at.Ticks + TimeSpan.TicksPerSecond - 1) / TimeSpan.TicksPerSecond);
}
