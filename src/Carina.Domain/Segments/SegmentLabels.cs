namespace Carina.Domain.Segments;

/// <summary>
/// Labels the segments of one recording to learn from, by the catalog and by what viewers did, never by a
/// person saying what a stretch is. The catalog labels at its entry's confidence, never above
/// <see cref="MostCatalogWeight"/>, and an entry whose kind or confidence is not known labels nothing.
/// Viewers label at a tenth or two: a skip nobody came back from labels what was skipped at
/// <see cref="SkippedWeight"/>; coming back labels <see cref="ReturnedSpan"/> from where the viewer came
/// back as programme at <see cref="ReturnedWeight"/>, and takes the skip it came back from, the latest
/// earlier one of the same kind that landed past where they came back to; closing the button on a break
/// and watching it labels it programme at <see cref="DismissedWeight"/>. A segment takes a label when its
/// kind covers more than half of it. In a recording the catalog is sure of an advert in
/// (<see cref="CatalogTruth.Sure"/>), a segment inside the guide's programme, reaching out of it by no more than
/// <see cref="GuideTolerance"/>, that no stretch of the catalog touches, whatever its kind, and no viewer's action
/// lies over, is the programme at <see cref="UncoveredWeight"/>, or at <see cref="UncoveredWithLogoWeight"/>
/// where the station's logo shows through at least <see cref="LogoSeenThrough"/> of it; with no logo known for
/// the station it stays at the lower weight. The catalog wins over viewers and viewers over a segment no stretch touches; within one source
/// the heavier label wins, a tie going to the programme. A segment more than half outside the guide's programme
/// is never a label.
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
        IReadOnlyList<CatalogHeard>? touching = null)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        Stretch[] heard = [.. context.Catalog.Select(Heard).OfType<Stretch>()];
        Stretch[] viewed = Viewed(actions);
        CatalogHeard[]? touched = context.Catalog.Any(stretch => CatalogTruth.Sure(stretch) is SegmentKind.Break)
            ? [.. touching ?? context.Catalog]
            : null;

        return
        [
            .. pieces.Select(piece => piece.Features[SegmentFeature.OutsideGuide] > MostOutsideTheGuide
                ? null
                : Over(piece, heard, SegmentLabelSource.Catalog) ?? Over(piece, viewed, SegmentLabelSource.Viewer) ?? Uncovered(piece, touched, viewed)),
        ];
    }

    /// <summary>
    /// The programme a segment is taken for when the recording holds an advert the catalog is sure of and neither
    /// a stretch of <paramref name="touched"/> nor what a viewer did lies over the segment, inside the guide's
    /// programme but for <see cref="GuideTolerance"/>; none when <paramref name="touched"/> is none, which is when the recording holds no such advert.
    /// </summary>
    private static SegmentLabel? Uncovered(SegmentPiece piece, CatalogHeard[]? touched, Stretch[] viewed)
    {
        if (touched is null
            || !InsideTheGuide(piece)
            || touched.Any(stretch => stretch.Starts < piece.Ends && stretch.Ends > piece.Starts)
            || viewed.Any(stretch => stretch.Starts < piece.Ends && stretch.Ends > piece.Starts))
        {
            return null;
        }

        double weight = piece.Features[SegmentFeature.LogoShare] >= LogoSeenThrough ? UncoveredWithLogoWeight : UncoveredWeight;

        return new SegmentLabel(SegmentKind.Programme, weight, SegmentLabelSource.Uncovered);
    }

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

    private static SegmentLabel? Over(SegmentPiece piece, Stretch[] stretches, SegmentLabelSource source)
        => Over(piece.Starts, piece.Ends, stretches, source);

    private static SegmentLabel? Over(TimeSpan starts, TimeSpan ends, Stretch[] stretches, SegmentLabelSource source)
    {
        Stretch[] over = [.. stretches.Where(stretch => stretch.Starts < ends && stretch.Ends > starts)];

        return over
            .GroupBy(stretch => stretch.Kind)
            .Where(kind => Covered(starts, ends, kind) * 2 > ends - starts)
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
