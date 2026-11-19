namespace Carina.Domain.Segments;

public readonly record struct SoundPlace(long Silence, int Dropped, LearningDataGap? Gap);

public sealed class SoundPlacement
{
    public static readonly TimeSpan Sway = TimeSpan.FromMilliseconds(10);

    private static readonly long SwayPairs = Pairs(Sway);

    private static readonly long ShortestGapPairs = Pairs(LearningDataGaps.Shortest);

    public long Placed { get; private set; }

    public TimeSpan PlacedThrough => Time(Placed);

    public SoundPlace Place(TimeSpan at, int pairs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(pairs);

        long starts = Pairs(at);
        long ahead = starts - Placed;

        if (Math.Abs(ahead) <= SwayPairs)
        {
            Placed += pairs;

            return new SoundPlace(0, 0, null);
        }

        if (ahead < 0)
        {
            int dropped = (int)Math.Min(-ahead, pairs);
            Placed += pairs - dropped;

            return new SoundPlace(0, dropped, null);
        }

        LearningDataGap? gap = ahead >= ShortestGapPairs ? new LearningDataGap(Time(Placed), Time(starts)) : null;
        Placed = starts + pairs;

        return new SoundPlace(ahead, 0, gap);
    }

    public static TimeSpan Time(long pairs)
        => TimeSpan.FromTicks((long)((Int128)pairs * TimeSpan.TicksPerSecond / SoundReader.SampleRate));

    private static long Pairs(TimeSpan at)
    {
        Int128 scaled = (Int128)at.Ticks * SoundReader.SampleRate;
        Int128 half = TimeSpan.TicksPerSecond / 2;

        return (long)((scaled >= 0 ? scaled + half : scaled - half) / TimeSpan.TicksPerSecond);
    }
}
