namespace Carina.Domain.Segments;

/// <summary>
/// Where a kind starts or ends in what the catalog is sure of: the moment, the kind, and whether the kind starts
/// there or ends there.
/// </summary>
public readonly record struct TruthBoundary(TimeSpan At, SegmentKind Kind, bool Enters);

/// <summary>
/// What the catalog is sure of in one recording, which a detection made without the catalog is measured against
/// (BR-CD-008): the stretches it hears an advert in from an entry at least <see cref="SureOfBreaks"/> sure, which
/// only an advert heard in two series or on two stations is, and those it hears an opening or an ending in from an
/// entry at least <see cref="SureOfThemes"/> sure, each as the kind it counts as there; a stretch taken for the
/// programme or for an advert by its place alone is left out. Stretches of one kind less than
/// <see cref="JoinedWithin"/> apart are one, since a match is cut to the sound it hears and the silence between two
/// adverts falls outside both. A boundary is where one starts or ends, except one less than
/// <see cref="JoinedWithin"/> from the recording's zero or from its end, where no detection draws one.
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
    /// What the catalog is sure of among the stretches it hears in a recording of <paramref name="length"/>.
    /// </summary>
    public static CatalogTruth Of(IReadOnlyList<CatalogHeard> heard, TimeSpan length)
    {
        ArgumentNullException.ThrowIfNull(heard);

        SegmentSpan[] stretches =
        [
            .. heard
                .Select(stretch => Sure(stretch) is SegmentKind kind ? new SegmentSpan(stretch.Starts, stretch.Ends, kind, null) : null)
                .OfType<SegmentSpan>()
                .GroupBy(stretch => stretch.Kind)
                .SelectMany(Joined)
                .OrderBy(stretch => stretch.Starts)
                .ThenBy(stretch => stretch.Kind),
        ];
        TruthBoundary[] boundaries =
        [
            .. stretches
                .SelectMany(stretch => new[] { new TruthBoundary(stretch.Starts, stretch.Kind, true), new TruthBoundary(stretch.Ends, stretch.Kind, false) })
                .Where(boundary => boundary.At >= JoinedWithin && boundary.At <= length - JoinedWithin)
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

    private static IEnumerable<SegmentSpan> Joined(IEnumerable<SegmentSpan> ofOneKind)
    {
        SegmentSpan? open = null;

        foreach (SegmentSpan stretch in ofOneKind.OrderBy(stretch => stretch.Starts))
        {
            if (open is not null && stretch.Starts - open.Ends < JoinedWithin)
            {
                open = new SegmentSpan(open.Starts, stretch.Ends > open.Ends ? stretch.Ends : open.Ends, open.Kind, null);

                continue;
            }

            if (open is not null)
            {
                yield return open;
            }

            open = stretch;
        }

        if (open is not null)
        {
            yield return open;
        }
    }
}
