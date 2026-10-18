namespace Carina.Domain.Segments;

/// <summary>
/// The two spans a rollback compares: the version in use from when it was put in use up to now, and the
/// version before it over <see cref="SegmentRollback.Within"/> before that, each from and up to and including.
/// </summary>
public sealed record RollbackSpans(DateTime From, DateTime Until, DateTime BeforeFrom, DateTime BeforeUntil);

/// <summary>
/// Why the version in use goes back to the one before it: within <see cref="Within"/> of being put in
/// use, viewers came back from more than <see cref="MostTimesThePrevious"/> times the share of its skips
/// that they came back from on the version before it over the same span before the change, over at least
/// <see cref="FewestSkips"/> skips. The shares, the skips and the version gone back to are kept with the
/// history. A version chosen by hand is never moved, nothing is compared with a version before it that no
/// viewer skipped with, and a version put back in use by a rollback is never rolled back to the version it
/// was put back from.
/// </summary>
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

    /// <summary>
    /// Judges a rollback on the skips and returns counted over the two <see cref="Spans"/>: those on the
    /// buttons of the version in use since it was put in use, and those on the buttons of the version before it.
    /// </summary>
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

    /// <summary>
    /// The spans the version in use is judged over at <paramref name="now"/>; none while it is not judged: chosen
    /// by hand, not yet put in use, or in use for longer than <see cref="Within"/>.
    /// </summary>
    public static RollbackSpans? Spans(SegmentModelVersion active, DateTime now)
    {
        DateTime adopted = InUseSince(active);

        return active.ByHand || now < adopted || now - adopted > Within
            ? null
            : new RollbackSpans(adopted, now, adopted - Within, adopted.AddTicks(-1));
    }

    /// <summary>
    /// The version the one in use took the place of, among the versions <paramref name="kept"/>: the one taken out
    /// of use as it was put in use. None when it took no other's place, or when it was put back in use by a
    /// rollback from that one.
    /// </summary>
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

    /// <summary>
    /// Whether the share that came back since is more than one and a half times the share before, in whole
    /// numbers, so that exactly one and a half times never rounds over.
    /// </summary>
    private static bool MoreThanTimesThePrevious(SkipReturns since, SkipReturns before)
        => (long)TimesUnder * since.Returned * before.Skips > (long)TimesOver * before.Returned * since.Skips;
}
