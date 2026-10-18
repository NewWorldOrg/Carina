namespace Carina.Domain.Encodings;

public enum ChapterRewriteVerdict
{
    Due = 1,

    AsCarried = 2,

    RewrittenToday = 3,
}

/// <summary>
/// Whether the chapters an artefact carries are written again to be the ones it would embed now: when there are
/// more or fewer of them, one is of another kind, or a boundary between two has moved by
/// <see cref="BoundaryMoves"/> or more, and the artefact was not written again within <see cref="OncePer"/>.
/// Both are read on the artefact's own clock, which is the recording's own time less the same head skip, so the
/// boundaries are as far apart on either. Where the first starts and the last ends are the artefact's own ends,
/// not boundaries.
/// </summary>
public static class ChapterRewrite
{
    public static readonly TimeSpan BoundaryMoves = TimeSpan.FromSeconds(1);

    public static readonly TimeSpan OncePer = TimeSpan.FromDays(1);

    public static ChapterRewriteVerdict Judge(
        IReadOnlyList<ChapterSegment> carried,
        IReadOnlyList<ChapterSegment> wanted,
        DateTime? lastRewrittenAt,
        DateTime now)
    {
        if (!Differ(carried, wanted))
        {
            return ChapterRewriteVerdict.AsCarried;
        }

        return lastRewrittenAt is { } at && now - at < OncePer ? ChapterRewriteVerdict.RewrittenToday : ChapterRewriteVerdict.Due;
    }

    public static bool Differ(IReadOnlyList<ChapterSegment> carried, IReadOnlyList<ChapterSegment> wanted)
    {
        ArgumentNullException.ThrowIfNull(carried);
        ArgumentNullException.ThrowIfNull(wanted);

        return carried.Count != wanted.Count
               || carried.Zip(wanted).Any(pair => pair.First.Kind != pair.Second.Kind)
               || Boundaries(carried).Zip(Boundaries(wanted)).Any(pair => (pair.First - pair.Second).Duration() >= BoundaryMoves);
    }

    private static IEnumerable<TimeSpan> Boundaries(IReadOnlyList<ChapterSegment> chapters)
        => chapters.Skip(1).Select(chapter => chapter.Starts).Concat(chapters.SkipLast(1).Select(chapter => chapter.Ends));
}
