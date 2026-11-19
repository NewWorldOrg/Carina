namespace Carina.Domain.Encodings;

public enum ChapterRewriteVerdict
{
    Due = 1,

    AsCarried = 2,

    RewrittenToday = 3,
}

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
