using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// Where one run put the breaks in one artefact. A marked reading covers the whole artefact end to
/// end with no gap, turning from programme to break and back at every mark; every other verdict
/// carries no segments.
/// </summary>
/// <remarks>
/// <see cref="BreakShare"/> is the share of the length the reading took for breaks, kept whatever
/// the verdict. <see cref="Noting"/> records that the reading was made from part of the evidence.
/// <see cref="Learned"/> is the station watermark the run learned from this source, for judging
/// later recordings of the same service; this reading is never judged by it.
/// </remarks>
public sealed record ChapterDetection
{
    private ChapterDetection(
        ChapterVerdict verdict,
        IReadOnlyList<ChapterSegment> segments,
        double breakShare,
        string note,
        WatermarkMask? learned)
    {
        Verdict = verdict;
        Segments = segments;
        BreakShare = breakShare;
        Note = note;
        Learned = learned;
    }

    public static ChapterDetection NotAsked { get; } =
        new(ChapterVerdict.NotAsked, [], 0, string.Empty, null);

    public ChapterVerdict Verdict { get; }

    public IReadOnlyList<ChapterSegment> Segments { get; }

    public double BreakShare { get; }

    public string Note { get; }

    public WatermarkMask? Learned { get; }

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

        return new ChapterDetection(ChapterVerdict.Marked, [.. segments], Shared(breakShare), string.Empty, null);
    }

    /// <summary>
    /// The same reading with a note that only part of the evidence was looked at, and why. The note is
    /// kept whatever the verdict.
    /// </summary>
    public ChapterDetection Noting(string note)
    {
        ArgumentException.ThrowIfNullOrEmpty(note);

        return new ChapterDetection(
            Verdict,
            Segments,
            BreakShare,
            Shortened(Note.Length is 0 ? note : $"{Note}; {note}"),
            Learned);
    }

    public ChapterDetection Learning(WatermarkMask learned)
    {
        ArgumentNullException.ThrowIfNull(learned);

        if (Verdict is ChapterVerdict.NotAsked)
        {
            throw new InvalidOperationException("Nobody looked at the source, so nothing was learned from it.");
        }

        return new ChapterDetection(Verdict, Segments, BreakShare, Note, learned);
    }

    public static ChapterDetection NothingFound()
        => new(ChapterVerdict.NothingFound, [], 0, string.Empty, null);

    public static ChapterDetection Discarded(double breakShare, string note)
        => new(ChapterVerdict.Discarded, [], Shared(breakShare), Shortened(note), null);

    public static ChapterDetection Unreadable(string note)
        => new(ChapterVerdict.Unreadable, [], 0, Shortened(note), null);

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
