using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// What a run has to say about where the breaks in one artefact are. A marked reading carries the
/// whole artefact laid out end to end with no gap, from its zero to its length, turning from
/// programme to break and back at every mark, and every other verdict carries nothing to mark: a
/// reading that was thrown away leaves no half of itself behind. <see cref="BreakShare"/> is how
/// much of the length the reading took for breaks, kept whatever the verdict, so the run that
/// tripped the safety valve can be found afterwards without reading logs.
/// </summary>
public sealed record ChapterDetection
{
    private ChapterDetection(
        ChapterVerdict verdict,
        IReadOnlyList<ChapterSegment> segments,
        double breakShare,
        string note)
    {
        Verdict = verdict;
        Segments = segments;
        BreakShare = breakShare;
        Note = note;
    }

    public static ChapterDetection NotAsked { get; } =
        new(ChapterVerdict.NotAsked, [], 0, string.Empty);

    public ChapterVerdict Verdict { get; }

    public IReadOnlyList<ChapterSegment> Segments { get; }

    public double BreakShare { get; }

    public string Note { get; }

    public bool Marks => Verdict is ChapterVerdict.Marked;

    public int Breaks => Segments.Count(segment => segment.Kind is ChapterKind.Break);

    public static ChapterDetection Marked(
        IReadOnlyList<ChapterSegment> segments,
        TimeSpan length,
        double breakShare)
    {
        ArgumentNullException.ThrowIfNull(segments);

        if (segments.Count is 0)
        {
            throw new ArgumentException("A marked reading marks something.", nameof(segments));
        }

        if (segments[0].Starts != TimeSpan.Zero)
        {
            throw new ArgumentException("A marked reading starts where the artefact starts.", nameof(segments));
        }

        for (int next = 1; next < segments.Count; next++)
        {
            if (segments[next].Starts != segments[next - 1].Ends)
            {
                throw new ArgumentException(
                    "A marked reading lays its chapters end to end, so one starts where the one before it ended.",
                    nameof(segments));
            }

            if (segments[next].Kind == segments[next - 1].Kind)
            {
                throw new ArgumentException(
                    "A marked reading turns from programme to break and back at every mark, so two chapters of one kind never sit side by side.",
                    nameof(segments));
            }
        }

        if (segments[^1].Ends != length)
        {
            throw new ArgumentException(
                "A marked reading covers the artefact to its end, so the last chapter ends where the artefact does.",
                nameof(segments));
        }

        if (!segments.Any(segment => segment.Kind is ChapterKind.Break))
        {
            throw new ArgumentException(
                "A marked reading found a break, so a reading of nothing but programme is not one.",
                nameof(segments));
        }

        return new ChapterDetection(ChapterVerdict.Marked, [.. segments], Shared(breakShare), string.Empty);
    }

    public static ChapterDetection NothingFound()
        => new(ChapterVerdict.NothingFound, [], 0, string.Empty);

    public static ChapterDetection Discarded(double breakShare, string note)
        => new(ChapterVerdict.Discarded, [], Shared(breakShare), Shortened(note));

    public static ChapterDetection Unreadable(string note)
        => new(ChapterVerdict.Unreadable, [], 0, Shortened(note));

    private static double Shared(double breakShare)
    {
        if (double.IsNaN(breakShare) || breakShare < 0 || breakShare > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(breakShare),
                breakShare,
                "A share of the length lies between none of it and all of it.");
        }

        return breakShare;
    }

    private static string Shortened(string note) => ProgrammeNote.Of(note, ProgrammeNote.Longest);
}
