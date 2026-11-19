namespace Carina.Domain.Segments;

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

    public const double ThemeTakenSurely = 0.8;

    public static readonly TimeSpan ReturnedSpan = TimeSpan.FromSeconds(10);

    public static readonly TimeSpan MostUncoveredOfJoined = BoundaryCandidates.MergedWithin * 2;

    public static readonly TimeSpan GuideTolerance = BoundaryCandidates.MergedWithin;

    public static IReadOnlyList<SegmentLabel?> Of(
        IReadOnlyList<SegmentPiece> pieces,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions,
        IReadOnlyList<CatalogHeard>? touching = null,
        ViewerSeeks? seeks = null,
        SegmentDetection? inUse = null)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        Stretch[] heard = [.. context.Catalog.Select(Heard).OfType<Stretch>()];
        Stretch[] viewed = [.. Viewed(actions), .. Sought(seeks ?? ViewerSeeks.None, context.Catalog)];
        CatalogHeard[]? touched = context.Catalog.Any(stretch => CatalogTruth.Sure(stretch) is SegmentKind.Break)
            ? [.. touching ?? context.Catalog]
            : null;
        Stretch[] themes = [.. SurelyThemes(inUse)];

        return
        [
            .. pieces.Select(piece => IsOutsideTheGuide(piece)
                ? null
                : Over(piece, heard, SegmentLabelSource.Catalog, MoreThanHalf) ?? Over(piece, viewed, SegmentLabelSource.Viewer, MoreThanHalf) ?? Uncovered(piece, touched, viewed, themes)),
        ];
    }

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

    private static SegmentLabel? Uncovered(SegmentPiece piece, CatalogHeard[]? touched, Stretch[] viewed, Stretch[] themes)
    {
        if (touched is null
            || !InsideTheGuide(piece)
            || Over(piece, themes, SegmentLabelSource.Uncovered, MoreThanHalf) is not null
            || IsShapedLikeAnAdvert(piece)
            || touched.Any(stretch => stretch.Starts < piece.Ends && stretch.Ends > piece.Starts)
            || viewed.Any(stretch => stretch.Starts < piece.Ends && stretch.Ends > piece.Starts))
        {
            return null;
        }

        double weight = piece.Features[SegmentFeature.LogoShare] >= LogoSeenThrough ? UncoveredWithLogoWeight : UncoveredWeight;

        return new SegmentLabel(SegmentKind.Programme, weight, SegmentLabelSource.Uncovered);
    }

    private static IEnumerable<Stretch> SurelyThemes(SegmentDetection? inUse)
        => inUse is null || inUse.State is SegmentDetectionState.Discarded
            ? []
            : inUse.Segments
                .Where(span => span.Kind is SegmentKind.Opening or SegmentKind.Ending && span.Probability >= ThemeTakenSurely)
                .Select(span => new Stretch(span.Starts, span.Ends, span.Kind, 1));

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
