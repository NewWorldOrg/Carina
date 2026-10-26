namespace Carina.Domain.Segments;

/// <summary>
/// Labels the segments of one recording to learn from, by the catalog and by what viewers did, never by a
/// person saying what a stretch is. The catalog labels at its entry's confidence, never above
/// <see cref="MostCatalogWeight"/>, and an entry whose kind or confidence is not known labels nothing.
/// Viewers label at a tenth or two: a skip nobody came back from labels what was skipped at
/// <see cref="SkippedWeight"/>; coming back labels <see cref="ReturnedSpan"/> from where the viewer came
/// back as programme at <see cref="ReturnedWeight"/>, and takes the skip it came back from, the latest
/// earlier one of the same kind that landed past where they came back to; closing the button on a break
/// and watching it labels it programme at <see cref="DismissedWeight"/>. A forward seek labels what it passed as
/// <see cref="ViewerSeeks"/> reads it, at a tenth or less, and coming back into it labels as coming back from a skip
/// does; both are viewers' labels. A segment takes a label when its
/// kind covers more than half of it. In a recording the catalog is sure of an advert in
/// (<see cref="CatalogTruth.Sure"/>), a segment inside the guide's programme, reaching out of it by no more than
/// <see cref="GuideTolerance"/>, that no stretch of the catalog touches, whatever its kind, and no viewer's action
/// lies over, and that is not a multiple of the grid between two silent cuts, which is likelier an advert the catalog
/// has not heard yet, is the programme at <see cref="UncoveredWeight"/>, or at <see cref="UncoveredWithLogoWeight"/>
/// where the station's logo shows through at least <see cref="LogoSeenThrough"/> of it; with no logo known for
/// the station it stays at the lower weight. The catalog wins over viewers and viewers over a segment no stretch touches; within one source
/// the heavier label wins, a tie going to the programme. A segment more than half outside the guide's programme
/// is never a label. Neighbouring segments read as one (<see cref="JoinedPiece"/>) take the catalog's kind only when its
/// stretches of that kind cover all of them but <see cref="MostUncoveredOfJoined"/>, since a run reaching past a sound
/// the catalog places is no example of it, and a viewer's as a segment does, since a skip starts where the viewer
/// pressed the button, partway into what it offered; they are never the programme by elimination, being read as one
/// only where an advert, an opening or an ending may lie across them, which is likelier one the catalog has not heard yet.
/// </summary>
public static class SegmentLabels
{
    public const double MostCatalogWeight = 0.9;

    public const double SkippedWeight = 0.2;

    public const double ReturnedWeight = 0.2;

    public const double DismissedWeight = 0.1;

    public const double MostOutsideTheGuide = 0.5;

    public const double UncoveredWeight = 0.3;

    public const double UncoveredWithLogoWeight = 0.5;

    public const double LogoSeenThrough = 0.8;

    public static readonly TimeSpan ReturnedSpan = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How much of neighbouring segments read as one the catalog's stretches of a kind may leave uncovered and still
    /// label them: as far as boundary candidates are merged, at each of the two ends.
    /// </summary>
    public static readonly TimeSpan MostUncoveredOfJoined = BoundaryCandidates.MergedWithin * 2;

    /// <summary>
    /// How far a segment may reach out of the guide's programme and still count as inside it for the programme by
    /// elimination: as far as boundary candidates are merged, so that a segment cut at a silence just before the
    /// guide's start or just after its end is not taken for one outside it.
    /// </summary>
    public static readonly TimeSpan GuideTolerance = BoundaryCandidates.MergedWithin;

    /// <summary>
    /// The labels of <paramref name="pieces"/> by what <paramref name="context"/> hears of the catalog and what
    /// viewers did. A segment is touched by the catalog when a stretch of <paramref name="touching"/> lies over any of
    /// it, by default the stretches the context hears; a stretch set aside from what the context hears still
    /// touches the segments it lies over, so that it never leaves programme behind.
    /// </summary>
    public static IReadOnlyList<SegmentLabel?> Of(
        IReadOnlyList<SegmentPiece> pieces,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions,
        IReadOnlyList<CatalogHeard>? touching = null,
        ViewerSeeks? seeks = null)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        Stretch[] heard = [.. context.Catalog.Select(Heard).OfType<Stretch>()];
        Stretch[] viewed = [.. Viewed(actions), .. Sought(seeks ?? ViewerSeeks.None, context.Catalog)];
        CatalogHeard[]? touched = context.Catalog.Any(stretch => CatalogTruth.Sure(stretch) is SegmentKind.Break)
            ? [.. touching ?? context.Catalog]
            : null;

        return
        [
            .. pieces.Select(piece => IsOutsideTheGuide(piece)
                ? null
                : Over(piece, heard, SegmentLabelSource.Catalog, MoreThanHalf) ?? Over(piece, viewed, SegmentLabelSource.Viewer, MoreThanHalf) ?? Uncovered(piece, touched, viewed)),
        ];
    }

    /// <summary>
    /// The labels of <paramref name="joined"/> by what <paramref name="context"/> hears of the catalog and what viewers
    /// did, each read as one segment from the start of its first to the end of its last, which the catalog labels only
    /// when its stretches of a kind leave no more than <see cref="MostUncoveredOfJoined"/> of it uncovered; none is the
    /// programme by elimination.
    /// </summary>
    public static IReadOnlyList<SegmentLabel?> OfJoined(
        IReadOnlyList<JoinedPiece> joined,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions,
        ViewerSeeks? seeks = null)
    {
        ArgumentNullException.ThrowIfNull(joined);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        Stretch[] heard = [.. context.Catalog.Select(Heard).OfType<Stretch>()];
        Stretch[] viewed = [.. Viewed(actions), .. Sought(seeks ?? ViewerSeeks.None, context.Catalog)];

        return
        [
            .. joined.Select(whole => IsOutsideTheGuide(whole.Piece)
                ? null
                : Over(whole.Piece, heard, SegmentLabelSource.Catalog, AllButTheEnds) ?? Over(whole.Piece, viewed, SegmentLabelSource.Viewer, MoreThanHalf)),
        ];
    }

    private static bool IsOutsideTheGuide(SegmentPiece piece) => piece.Features[SegmentFeature.OutsideGuide] > MostOutsideTheGuide;

    private static bool MoreThanHalf(TimeSpan covered, TimeSpan length) => covered * 2 > length;

    private static bool AllButTheEnds(TimeSpan covered, TimeSpan length) => length - covered <= MostUncoveredOfJoined;

    /// <summary>
    /// The programme a segment is taken for when the recording holds an advert the catalog is sure of and neither
    /// a stretch of <paramref name="touched"/> nor what a viewer did lies over the segment, inside the guide's
    /// programme but for <see cref="GuideTolerance"/>, off the grid or with an edge that is no silent cut; none when
    /// <paramref name="touched"/> is none, which is when the recording holds no such advert.
    /// </summary>
    private static SegmentLabel? Uncovered(SegmentPiece piece, CatalogHeard[]? touched, Stretch[] viewed)
    {
        if (touched is null
            || !InsideTheGuide(piece)
            || IsShapedLikeAnAdvert(piece)
            || touched.Any(stretch => stretch.Starts < piece.Ends && stretch.Ends > piece.Starts)
            || viewed.Any(stretch => stretch.Starts < piece.Ends && stretch.Ends > piece.Starts))
        {
            return null;
        }

        double weight = piece.Features[SegmentFeature.LogoShare] >= LogoSeenThrough ? UncoveredWithLogoWeight : UncoveredWeight;

        return new SegmentLabel(SegmentKind.Programme, weight, SegmentLabelSource.Uncovered);
    }

    private static bool IsShapedLikeAnAdvert(SegmentPiece piece)
        => piece.Features[SegmentFeature.EdgeSilence] is 1.0 && piece.Features[SegmentFeature.IsMultipleOf15s] is 1.0;

    private static bool InsideTheGuide(SegmentPiece piece)
        => piece.Features[SegmentFeature.OutsideGuide] is double share && piece.Length * share <= GuideTolerance;

    private static Stretch? Heard(CatalogHeard heard)
        => heard is { Kind: SegmentKind kind, Confidence: double confidence }
            ? new Stretch(heard.Starts, heard.Ends, kind, Math.Min(confidence, MostCatalogWeight))
            : null;

    private static Stretch[] Viewed(IReadOnlyList<ViewerAction> actions)
    {
        ViewerAction[] ordered = [.. actions.OrderBy(action => action.HappenedAt)];
        HashSet<ViewerAction> cameBackFrom = new(ReferenceEqualityComparer.Instance);
        List<Stretch> viewed = [];

        foreach (ViewerAction returned in ordered.Where(action => action.Action is ViewerActionKind.Returned))
        {
            ViewerAction? skip = ordered.LastOrDefault(action => action.Action is ViewerActionKind.Skipped
                                                                  && action.Kind == returned.Kind
                                                                  && action.HappenedAt <= returned.HappenedAt
                                                                  && returned.To < action.To
                                                                  && !cameBackFrom.Contains(action));

            if (skip is not null)
            {
                cameBackFrom.Add(skip);
            }

            viewed.Add(new Stretch(returned.To, returned.To + ReturnedSpan, SegmentKind.Programme, ReturnedWeight));
        }

        viewed.AddRange(ordered
            .Where(action => action.Action is ViewerActionKind.Skipped && action.To > action.From && !cameBackFrom.Contains(action))
            .Select(action => new Stretch(action.From, action.To, action.Kind, SkippedWeight)));
        viewed.AddRange(ordered
            .Where(action => action is { Action: ViewerActionKind.Dismissed, Kind: SegmentKind.Break } && action.To > action.From)
            .Select(action => new Stretch(action.From, action.To, SegmentKind.Programme, DismissedWeight)));

        return [.. viewed];
    }

    /// <summary>
    /// What the seeks passed, each of the kind <see cref="ViewerSeeks.KindOf"/> gives it by <paramref name="catalog"/>
    /// at its own weight, and <see cref="ReturnedSpan"/> from where a viewer came back as the programme at
    /// <see cref="ReturnedWeight"/>.
    /// </summary>
    private static IEnumerable<Stretch> Sought(ViewerSeeks seeks, IReadOnlyList<CatalogHeard> catalog)
        =>
        [
            .. seeks.Spans
                .Select(span => (Span: span, Kind: ViewerSeeks.KindOf(span, catalog)))
                .Where(read => read.Kind is not null)
                .Select(read => new Stretch(read.Span.Starts, read.Span.Ends, read.Kind!.Value, read.Span.Weight)),
            .. seeks.CameBackTo.Select(at => new Stretch(at, at + ReturnedSpan, SegmentKind.Programme, ReturnedWeight)),
        ];

    private static SegmentLabel? Over(SegmentPiece piece, Stretch[] stretches, SegmentLabelSource source, Func<TimeSpan, TimeSpan, bool> enough)
    {
        TimeSpan starts = piece.Starts;
        TimeSpan ends = piece.Ends;
        Stretch[] over = [.. stretches.Where(stretch => stretch.Starts < ends && stretch.Ends > starts)];

        return over
            .GroupBy(stretch => stretch.Kind)
            .Where(kind => enough(Covered(starts, ends, kind), ends - starts))
            .Select(kind => new SegmentLabel(kind.Key, kind.Max(stretch => stretch.Weight), source))
            .OrderByDescending(label => label.Weight)
            .ThenBy(label => label.Kind)
            .FirstOrDefault();
    }

    private static TimeSpan Covered(TimeSpan starts, TimeSpan ends, IEnumerable<Stretch> stretches)
    {
        TimeSpan covered = TimeSpan.Zero;
        TimeSpan reached = starts;

        foreach (Stretch stretch in stretches.OrderBy(stretch => stretch.Starts))
        {
            TimeSpan from = stretch.Starts > reached ? stretch.Starts : reached;
            TimeSpan to = stretch.Ends < ends ? stretch.Ends : ends;

            if (to > from)
            {
                covered += to - from;
                reached = to;
            }
        }

        return covered;
    }

    private readonly record struct Stretch(TimeSpan Starts, TimeSpan Ends, SegmentKind Kind, double Weight);
}
