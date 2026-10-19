namespace Carina.Domain.Segments;

/// <summary>
/// The seconds of one kind the catalog is sure of that were detected as it, as a share of those seconds; a share of
/// nothing is not known.
/// </summary>
public sealed record KindMetrics(SegmentKind Kind, double? Recall);

/// <summary>
/// How a version detects the recordings it is judged on, measured against what the catalog is sure of in them
/// (<see cref="CatalogTruth"/>): the share of its boundaries with a detected boundary within <see cref="NearWithin"/>
/// and within <see cref="CloseWithin"/> turning the same way, that is into the kind where the kind starts and out of
/// it where it ends, not known without one; the share of its seconds of breaks, of openings and of endings detected as
/// the same kind, a kind the catalog is sure of nowhere not known; and the share of skips on the version's buttons that
/// viewers came back from, and how many skips that is of. A detection thrown away shows nothing, so it has no
/// boundaries and finds no second. What a detection finds beyond what the catalog is sure of is not counted, since it
/// cannot be told from an advert the catalog has not heard yet.
/// </summary>
public sealed record SegmentMetrics
{
    public static readonly TimeSpan NearWithin = TimeSpan.FromSeconds(2);

    public static readonly TimeSpan CloseWithin = TimeSpan.FromSeconds(1);

    private static readonly SegmentKind[] Measured = [SegmentKind.Break, SegmentKind.Opening, SegmentKind.Ending];

    public SegmentMetrics(
        double? boundaryRecallWithin2s,
        double? boundaryRecallWithin1s,
        IReadOnlyList<KindMetrics> kinds,
        double? returnedAfterSkipShare,
        int skips)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentOutOfRangeException.ThrowIfNegative(skips);

        BoundaryRecallWithin2s = boundaryRecallWithin2s;
        BoundaryRecallWithin1s = boundaryRecallWithin1s;
        Kinds = [.. kinds];
        ReturnedAfterSkipShare = returnedAfterSkipShare;
        Skips = skips;
    }

    public double? BoundaryRecallWithin2s { get; }

    public double? BoundaryRecallWithin1s { get; }

    public IReadOnlyList<KindMetrics> Kinds { get; }

    public double? ReturnedAfterSkipShare { get; }

    public int Skips { get; }

    /// <summary>
    /// The share of the seconds of adverts the catalog is sure of that were detected as breaks.
    /// </summary>
    public double? BreakRecall => Kinds.FirstOrDefault(kind => kind.Kind is SegmentKind.Break)?.Recall;

    /// <summary>
    /// These figures beside what viewers did with the version's buttons as <paramref name="returns"/> counts.
    /// </summary>
    public SegmentMetrics WithReturns(SkipReturns returns)
    {
        ArgumentNullException.ThrowIfNull(returns);

        return new SegmentMetrics(BoundaryRecallWithin2s, BoundaryRecallWithin1s, Kinds, returns.Share, returns.Skips);
    }

    /// <summary>
    /// The figures of <paramref name="compared"/>, beside what viewers did with the version's buttons; none for no
    /// recording.
    /// </summary>
    public static SegmentMetrics? Of(IReadOnlyList<SegmentComparison> compared, SkipReturns returns)
    {
        ArgumentNullException.ThrowIfNull(compared);
        ArgumentNullException.ThrowIfNull(returns);

        if (compared.Count is 0)
        {
            return null;
        }

        Boundary[][] detected = [.. compared.Select(comparison => Boundaries(comparison.Shown))];

        return new SegmentMetrics(
            Recall(compared, detected, NearWithin),
            Recall(compared, detected, CloseWithin),
            [.. Measured.Select(kind => Seconds(compared, kind))],
            returns.Share,
            returns.Skips);
    }

    /// <summary>
    /// Where the kind turns in stretches laid end to end, and which way.
    /// </summary>
    private static Boundary[] Boundaries(IReadOnlyList<SegmentSpan> spans)
        => [.. spans.Zip(spans.Skip(1)).Where(pair => pair.First.Kind != pair.Second.Kind).Select(pair => new Boundary(pair.Second.Starts, pair.First.Kind, pair.Second.Kind))];

    private static double? Recall(IReadOnlyList<SegmentComparison> compared, Boundary[][] detected, TimeSpan within)
    {
        int total = compared.Sum(comparison => comparison.Truth.Boundaries.Count);
        int found = compared
            .Select((comparison, at) => comparison.Truth.Boundaries.Count(truth => detected[at].Any(boundary => boundary.Turns(truth) && Near(truth.At, boundary.At, within))))
            .Sum();

        return total is 0 ? null : found / (double)total;
    }

    private static KindMetrics Seconds(IReadOnlyList<SegmentComparison> compared, SegmentKind kind)
    {
        double sure = compared.Sum(comparison => Of(comparison.Truth.Stretches, kind).Sum(span => span.Length.TotalSeconds));
        double found = compared.Sum(comparison => Overlap(Of(comparison.Truth.Stretches, kind), Of(comparison.Shown, kind)));

        return new KindMetrics(kind, sure > 0 ? found / sure : null);
    }

    private static SegmentSpan[] Of(IReadOnlyList<SegmentSpan> spans, SegmentKind kind) => [.. spans.Where(span => span.Kind == kind)];

    private static double Overlap(SegmentSpan[] one, SegmentSpan[] other)
        => one.Sum(span => other.Sum(against => Math.Max(0, ((span.Ends < against.Ends ? span.Ends : against.Ends) - (span.Starts > against.Starts ? span.Starts : against.Starts)).TotalSeconds)));

    private static bool Near(TimeSpan one, TimeSpan other, TimeSpan within) => (one - other).Duration() <= within;

    private readonly record struct Boundary(TimeSpan At, SegmentKind From, SegmentKind To)
    {
        /// <summary>
        /// Whether this boundary turns into the kind where <paramref name="truth"/> starts it, or out of it where
        /// <paramref name="truth"/> ends it.
        /// </summary>
        public bool Turns(TruthBoundary truth) => truth.Enters ? To == truth.Kind : From == truth.Kind;
    }
}
