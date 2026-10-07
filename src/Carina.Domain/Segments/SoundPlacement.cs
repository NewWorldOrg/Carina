namespace Carina.Domain.Segments;

/// <summary>
/// Where a block of sound goes: how many pairs of silence are put before it, how many of its own
/// pairs are dropped from its head, and the gap the silence stands for when it is long enough to be
/// one.
/// </summary>
public readonly record struct SoundPlace(long Silence, int Dropped, LearningDataGap? Gap);

/// <summary>
/// Places sound handed over in blocks, each with the time of its first pair of samples, on the
/// recording's own time, counted in pairs of <see cref="SoundReader.SampleRate"/> a second from
/// zero. A block that starts within <see cref="Sway"/> of where the sound placed so far ends carries
/// straight on from there. One that starts later is put where it starts, after silence, and the
/// silence is a gap when it lasts at least <see cref="LearningDataGaps.Shortest"/>. One that starts
/// earlier loses the pairs that would fall on sound already placed.
/// </summary>
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
