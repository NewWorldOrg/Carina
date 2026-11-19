using Carina.Domain.Captions;

namespace Carina.Domain.Segments;

public static class CaptionPresence
{
    public const byte Hidden = 0;

    public const byte Shown = 1;

    public const int PerChunk = LearningData.ChunkSeconds;

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
