namespace Carina.Domain.Segments;

/// <summary>
/// How a detection of one recording ended: breaks marked, only the programme found, or the sequence
/// thrown away.
/// </summary>
public enum SegmentDetectionState
{
    Marked = 1,

    NothingFound = 2,

    Discarded = 3,
}

/// <summary>
/// The detection of one recording: its stretches end to end from the recording's zero to its length,
/// turning kind at every boundary; the share of the length breaks take, whatever the state; and the
/// boundary candidates' seconds, both kept to <see cref="SegmentSequence.Resolution"/>. The sequence is
/// thrown away when breaks take more than <see cref="MostBreakShare"/> of the length (exactly that much
/// is kept) or there are more than <see cref="MostStretchesAnHour"/> stretches an hour; a sequence thrown
/// away is kept as it was.
/// </summary>
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
        SegmentDetectionState state = segments.Any(segment => segment.Kind is SegmentKind.Break)
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
