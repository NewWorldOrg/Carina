namespace Carina.Domain.Segments;

public enum SegmentDetectionState
{
    Marked = 1,

    NothingFound = 2,

    Discarded = 3,
}

public sealed class SegmentDetection
{
    public const double MostBreakShare = 0.5;

    public const int MostStretchesAnHour = 80;

    private SegmentDetection(SegmentDetectionState state, IReadOnlyList<SegmentSpan> segments, double cmShare, IReadOnlyList<TimeSpan> snaps)
    {
        State = state;
        Segments = segments;
        CmShare = cmShare;
        Snaps = snaps;
    }

    public SegmentDetectionState State { get; }

    public IReadOnlyList<SegmentSpan> Segments { get; }

    public double CmShare { get; }

    public IReadOnlyList<TimeSpan> Snaps { get; }

    public static SegmentDetection Judged(IReadOnlyList<SegmentSpan> segments, TimeSpan length, IReadOnlyList<TimeSpan> snaps)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(snaps);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero);
        Tiled(segments, length);

        long breaks = segments.Where(segment => segment.Kind is SegmentKind.Break).Sum(segment => segment.Length.Ticks);
        bool discarded = breaks > length.Ticks * MostBreakShare
            || (long)segments.Count * TimeSpan.TicksPerHour > MostStretchesAnHour * length.Ticks;
        SegmentDetectionState state = segments.Any(segment => segment.Kind is not SegmentKind.Programme)
            ? SegmentDetectionState.Marked
            : SegmentDetectionState.NothingFound;

        return new SegmentDetection(
            discarded ? SegmentDetectionState.Discarded : state,
            [.. segments],
            Math.Clamp(breaks / (double)length.Ticks, 0, 1),
            [.. snaps.Select(Rounded)]);
    }

    private static void Tiled(IReadOnlyList<SegmentSpan> segments, TimeSpan length)
    {
        if (segments.Count is 0 || segments[0].Starts != TimeSpan.Zero || segments[^1].Ends != length)
        {
            throw new ArgumentException("A detection covers its recording from zero to its length.", nameof(segments));
        }

        for (int at = 1; at < segments.Count; at++)
        {
            if (segments[at].Starts != segments[at - 1].Ends || segments[at].Kind == segments[at - 1].Kind)
            {
                throw new ArgumentException("A detection lays its stretches end to end and turns kind at every boundary.", nameof(segments));
            }
        }
    }

    private static TimeSpan Rounded(TimeSpan at)
        => TimeSpan.FromTicks((long)Math.Round(at.Ticks / (double)SegmentSequence.Resolution.Ticks, MidpointRounding.AwayFromZero) * SegmentSequence.Resolution.Ticks);
}
