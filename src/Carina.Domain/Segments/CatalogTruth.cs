namespace Carina.Domain.Segments;

/// <summary>
/// Where a kind starts or ends in what the catalog is sure of: the moment, the kind, and whether the kind starts
/// there or ends there.
/// </summary>
public readonly record struct TruthBoundary(TimeSpan At, SegmentKind Kind, bool Enters);

/// <summary>
/// What the catalog is sure of in one recording, which a detection made without the catalog is measured against
/// (BR-CD-008). The stretches it hears of one kind, an advert, an opening or an ending, less than
/// <see cref="JoinedWithin"/> apart are one run, since a match is cut to the sound it hears and the silence between two
/// adverts falls outside both: a pod of adverts, or an opening or an ending heard in parts. The catalog is sure of a run
/// it hears an advert in from an entry at least <see cref="SureOfBreaks"/> sure, which only an advert heard in two series
/// or on two stations is, or an opening or an ending from an entry at least <see cref="SureOfThemes"/> sure, each as the
/// kind it counts as there; a stretch taken for the programme by its place alone is no part of a run. A boundary is
/// where a run starts or ends, the outer edges of a pod and not the joins between its adverts, since a viewer skips a
/// pod from one edge to the other; except one less than <see cref="JoinedWithin"/> from the recording's zero or from its
/// end, where no detection draws one, and one beside an advert: the stretch outside it, to the first seam more than
/// <see cref="JoinedWithin"/> beyond it, as long as an advert lasts (<see cref="SegmentGrid.IsAsLongAsAnAdvert"/>), where an
/// advert the catalog has not heard twice yet may carry the run on, so that a detection taking the whole pod would miss
/// the boundary for being right. A seam is a silent cut or one of the recording's own ends among the boundary candidates
/// of its sound and picture alone, read with <see cref="SegmentThresholds.Initial"/> whatever version is measured.
/// </summary>
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

    /// <summary>
    /// Whether the catalog is sure of anything in the recording, which is what a recording is judged on.
    /// </summary>
    public bool Judges => Stretches.Count is not 0;

    /// <summary>
    /// Whether the catalog is sure of an advert in the recording, which is what the evidence counts.
    /// </summary>
    public bool HoldsBreaks => Stretches.Any(stretch => stretch.Kind is SegmentKind.Break);

    public int BreakBoundaries => Boundaries.Count(boundary => boundary.Kind is SegmentKind.Break);

    /// <summary>
    /// What the catalog is sure of among the stretches it hears in a recording of <paramref name="evidence"/>.
    /// </summary>
    public static CatalogTruth Of(IReadOnlyList<CatalogHeard> heard, SegmentEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);

        return Of(heard, evidence.Length, BoundaryCandidates.Find(evidence, SegmentThresholds.Initial));
    }

    /// <summary>
    /// What the catalog is sure of among the stretches it hears in a recording of <paramref name="length"/> whose sound
    /// and picture alone give <paramref name="candidates"/>.
    /// </summary>
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

    /// <summary>
    /// The kind the catalog is sure a stretch it hears is; none when it is not sure enough of it.
    /// </summary>
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

    /// <summary>
    /// Whether the stretch outside <paramref name="boundary"/>, from it to the first of <paramref name="seams"/> more than
    /// <see cref="JoinedWithin"/> beyond it, is as long as an advert lasts.
    /// </summary>
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

    /// <summary>
    /// The runs of the stretches heard of one kind that the catalog is sure of: the stretches less than
    /// <see cref="JoinedWithin"/> apart joined, kept where one of them is sure.
    /// </summary>
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
