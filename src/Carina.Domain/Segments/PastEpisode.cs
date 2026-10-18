namespace Carina.Domain.Segments;

/// <summary>
/// An earlier episode of the series of the recording judged: where its programme starts in its own
/// recording's time, and the stretches it was taken to hold.
/// </summary>
public sealed record PastEpisode
{
    public PastEpisode(TimeSpan programmeStarts, IReadOnlyList<SegmentSpan> segments)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Any(segment => segment is null))
        {
            throw new ArgumentException("Every stretch of an episode is handed over.", nameof(segments));
        }

        ProgrammeStarts = programmeStarts;
        Segments = [.. segments];
    }

    public TimeSpan ProgrammeStarts { get; }

    public IReadOnlyList<SegmentSpan> Segments { get; }

    /// <summary>
    /// Whether a break of this episode lies within <paramref name="within"/> of the same distance from
    /// its programme's start as <paramref name="fromProgrammeStart"/>.
    /// </summary>
    public bool HadABreakNear(TimeSpan fromProgrammeStart, TimeSpan within)
    {
        TimeSpan from = ProgrammeStarts + fromProgrammeStart - within;
        TimeSpan to = ProgrammeStarts + fromProgrammeStart + within;

        return Segments.Any(segment => segment.Kind is SegmentKind.Break && segment.Starts <= to && segment.Ends >= from);
    }
}
