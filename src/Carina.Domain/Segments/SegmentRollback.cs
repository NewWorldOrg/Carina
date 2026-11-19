namespace Carina.Domain.Segments;

public sealed record RollbackSpans(DateTime From, DateTime Until, DateTime BeforeFrom, DateTime BeforeUntil);

public sealed record SegmentRollback(double ReturnedAfterSkipShare, double PreviousShare, int Skips, int BackTo)
{
    public const double MostTimesThePrevious = 1.5;

    public const int FewestSkips = 20;

    private const int TimesOver = 3;

    private const int TimesUnder = 2;

    public static readonly TimeSpan Within = TimeSpan.FromDays(7);

    public static SegmentRollback? Judge(SegmentModelVersion active, SegmentModelVersion previous, IReadOnlyList<ViewerAction> actions, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(actions);

        Compared(active, previous);

        return Spans(active, now) is { } spans
            ? Judge(
                active,
                previous,
                SkipReturns.Of(actions, active.Number, spans.From, spans.Until),
                SkipReturns.Of(actions, previous.Number, spans.BeforeFrom, spans.BeforeUntil),
                now)
            : null;
    }

    public static SegmentRollback? Judge(SegmentModelVersion active, SegmentModelVersion previous, SkipReturns since, SkipReturns before, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(since);
        ArgumentNullException.ThrowIfNull(before);

        Compared(active, previous);

        if (Spans(active, now) is null)
        {
            return null;
        }

        return since is { Skips: >= FewestSkips, Share: double share } && before.Share is double earlier && MoreThanTimesThePrevious(since, before)
            ? new SegmentRollback(share, earlier, since.Skips, previous.Number)
            : null;
    }

    public static RollbackSpans? Spans(SegmentModelVersion active, DateTime now)
    {
        DateTime adopted = InUseSince(active);

        return active.ByHand || now < adopted || now - adopted > Within
            ? null
            : new RollbackSpans(adopted, now, adopted - Within, adopted.AddTicks(-1));
    }

    public static SegmentModelVersion? Previous(IReadOnlyList<SegmentModelVersion> kept)
    {
        ArgumentNullException.ThrowIfNull(kept);

        if (kept.Where(version => version.State is SegmentModelVersionState.Active).ToArray() is not [SegmentModelVersion active])
        {
            throw new InvalidOperationException("Exactly one version is in use.");
        }

        SegmentModelVersion? left = kept
            .Where(version => version.State is SegmentModelVersionState.Retired && version.RetiredAt == active.AdoptedAt)
            .OrderByDescending(version => version.Number)
            .FirstOrDefault();

        return left is null || left.RolledBackAt == active.AdoptedAt ? null : left;
    }

    private static void Compared(SegmentModelVersion active, SegmentModelVersion previous)
    {
        ArgumentNullException.ThrowIfNull(previous);

        InUseSince(active);

        if (previous.Number == active.Number)
        {
            throw new ArgumentException("A version goes back to another.", nameof(previous));
        }
    }

    private static DateTime InUseSince(SegmentModelVersion active)
    {
        ArgumentNullException.ThrowIfNull(active);

        return active.State is SegmentModelVersionState.Active && active.AdoptedAt is DateTime adopted
            ? adopted
            : throw new ArgumentException("Only the version in use goes back.", nameof(active));
    }

    private static bool MoreThanTimesThePrevious(SkipReturns since, SkipReturns before)
        => (long)TimesUnder * since.Returned * before.Skips > (long)TimesOver * before.Returned * since.Skips;
}
