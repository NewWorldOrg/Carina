using Carina.Domain.Base;

namespace Carina.Domain.Encodings;

/// <summary>
/// What a run has to say about where the breaks in one artefact are. A marked reading carries the
/// whole artefact laid out end to end with no gap, from its zero to its length, turning from
/// programme to break and back at every mark, and every other verdict carries nothing to mark: a
/// reading that was thrown away leaves no half of itself behind. <see cref="BreakShare"/> is how
/// much of the length the reading took for breaks, kept whatever the verdict, so the run that
/// tripped the safety valve can be found afterwards without reading logs.
/// <see cref="Noting"/> is how a reading says that it was made from part of what there was to see:
/// it changes nothing that was read, only what is known about the reading of it.
/// <para>
/// <see cref="Learned"/> is the station's watermark the run learned from this source, for judging
/// the recordings of the same service that come after it. It rides beside the reading whatever the
/// verdict and is never what this reading was judged by.
/// </para>
/// </summary>
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
    /// The same reading with something further said about how it was made — that only part of what
    /// there was to see was looked at, and why. A reading made from part of the evidence is still
    /// the reading that evidence gives; what is added here is the standing to doubt it, kept
    /// whatever the verdict so that it is read wherever the reading is.
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
