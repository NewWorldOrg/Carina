namespace Carina.Domain.Segments;

/// <summary>
/// Why the version in use goes back to the one before it: within <see cref="Within"/> of being put in
/// use, viewers came back from more than <see cref="MostTimesThePrevious"/> times the share of its skips
/// that they came back from on the version before it over the same span before the change, over at least
/// <see cref="FewestSkips"/> skips. The shares, the skips and the version gone back to are kept with the
/// history. A version chosen by hand is never moved, and nothing is compared with a version before it
/// that no viewer skipped with.
/// </summary>
public sealed record SegmentRollback(double ReturnedAfterSkipShare, double PreviousShare, int Skips, int BackTo)
{
    public const double MostTimesThePrevious = 1.5;

    public const int FewestSkips = 20;

    public static readonly TimeSpan Within = TimeSpan.FromDays(7);

    public static SegmentRollback? Judge(SegmentModelVersion active, SegmentModelVersion previous, IReadOnlyList<ViewerAction> actions, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(actions);

        if (active.State is not SegmentModelVersionState.Active || active.AdoptedAt is not DateTime adopted)
        {
            throw new ArgumentException("Only the version in use goes back.", nameof(active));
        }

        if (previous.Number == active.Number)
        {
            throw new ArgumentException("A version goes back to another.", nameof(previous));
        }

        if (active.ByHand || now < adopted || now - adopted > Within)
        {
            return null;
        }

        SkipReturns since = SkipReturns.Of(actions, active.Number, adopted, now);
        SkipReturns before = SkipReturns.Of(actions, previous.Number, adopted - Within, adopted.AddTicks(-1));

        return since is { Skips: >= FewestSkips, Share: double share } && before.Share is double earlier && share > MostTimesThePrevious * earlier
            ? new SegmentRollback(share, earlier, since.Skips, previous.Number)
            : null;
    }
}
