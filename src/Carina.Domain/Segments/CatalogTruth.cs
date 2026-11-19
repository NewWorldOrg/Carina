namespace Carina.Domain.Segments;

public readonly record struct TruthBoundary(TimeSpan At, SegmentKind Kind, bool Enters);

public sealed class CatalogTruth
{
    public const double SureOfBreaks = 0.9;

    public const double SureOfThemes = 0.8;

    public static readonly TimeSpan JoinedWithin = TimeSpan.FromSeconds(2);

    private CatalogTruth(IReadOnlyList<SegmentSpan> stretches, IReadOnlyList<TruthBoundary> boundaries)
    {
        Stretches = stretches;
        Boundaries = boundaries;
    }

    public static CatalogTruth None { get; } = new([], []);

    public IReadOnlyList<SegmentSpan> Stretches { get; }

    public IReadOnlyList<TruthBoundary> Boundaries { get; }

    public bool Judges => Stretches.Count is not 0;

    public bool HoldsBreaks => Stretches.Any(stretch => stretch.Kind is SegmentKind.Break);

    public int BreakBoundaries => Boundaries.Count(boundary => boundary.Kind is SegmentKind.Break);

    public static CatalogTruth Of(IReadOnlyList<CatalogHeard> heard, SegmentEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return Of(heard, evidence.Length, BoundaryCandidates.Find(evidence, SegmentThresholds.Initial));
    }

    public static CatalogTruth Of(IReadOnlyList<CatalogHeard> heard, TimeSpan length, IReadOnlyList<BoundaryCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(heard);
        ArgumentNullException.ThrowIfNull(candidates);

        BoundaryCandidate[] seams = [.. candidates.Where(candidate => candidate.IsSilentCut || candidate.IsRecordingEdge).OrderBy(candidate => candidate.At)];
        SegmentSpan[] stretches =
        [
            .. heard
                .Where(stretch => stretch.Kind is SegmentKind.Break or SegmentKind.Opening or SegmentKind.Ending)
                .GroupBy(stretch => stretch.Kind!.Value)
                .SelectMany(Runs)
                .OrderBy(stretch => stretch.Starts)
                .ThenBy(stretch => stretch.Kind),
        ];
        TruthBoundary[] boundaries =
        [
            .. stretches
                .SelectMany(stretch => new[] { new TruthBoundary(stretch.Starts, stretch.Kind, true), new TruthBoundary(stretch.Ends, stretch.Kind, false) })
                .Where(boundary => boundary.At >= JoinedWithin && boundary.At <= length - JoinedWithin)
                .Where(boundary => !BesideAnAdvert(boundary, seams))
                .OrderBy(boundary => boundary.At)
                .ThenBy(boundary => boundary.Kind),
        ];

        return new CatalogTruth(stretches, boundaries);
    }

    public static SegmentKind? Sure(CatalogHeard heard)
    {
        ArgumentNullException.ThrowIfNull(heard);

        return heard switch
        {
            { Kind: SegmentKind.Break, Confidence: >= SureOfBreaks } => SegmentKind.Break,
            { Kind: SegmentKind.Opening or SegmentKind.Ending, Confidence: >= SureOfThemes } => heard.Kind,
            _ => null,
        };
    }

    private static bool BesideAnAdvert(TruthBoundary boundary, BoundaryCandidate[] seams)
    {
        BoundaryCandidate? next = boundary.Enters
            ? seams.LastOrDefault(seam => seam.At < boundary.At - JoinedWithin)
            : seams.FirstOrDefault(seam => seam.At > boundary.At + JoinedWithin);

        if (next is null)
        {
            return false;
        }

        TimeSpan shortest = boundary.Enters ? boundary.At - next.Latest : next.Earliest - boundary.At;
        TimeSpan longest = boundary.Enters ? boundary.At - next.Earliest : next.Latest - boundary.At;

        return SegmentGrid.IsAsLongAsAnAdvert(
            shortest > TimeSpan.Zero ? shortest : TimeSpan.Zero,
            longest,
            TimeSpan.FromSeconds(SegmentThresholds.Initial.GridToleranceSec));
    }

    private static IEnumerable<SegmentSpan> Runs(IGrouping<SegmentKind, CatalogHeard> ofOneKind)
    {
        SegmentSpan? open = null;
        bool sure = false;

        foreach (CatalogHeard stretch in ofOneKind.OrderBy(stretch => stretch.Starts))
        {
            if (open is not null && stretch.Starts - open.Ends < JoinedWithin)
            {
                open = new SegmentSpan(open.Starts, stretch.Ends > open.Ends ? stretch.Ends : open.Ends, ofOneKind.Key, null);
                sure |= Sure(stretch) is not null;

                continue;
            }

            if (open is not null && sure)
            {
                yield return open;
            }

            open = new SegmentSpan(stretch.Starts, stretch.Ends, ofOneKind.Key, null);
            sure = Sure(stretch) is not null;
        }

        if (open is not null && sure)
        {
            yield return open;
        }
    }
}
