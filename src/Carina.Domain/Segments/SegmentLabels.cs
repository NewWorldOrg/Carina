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
/// kind covers more than half of it. The catalog wins over viewers; within one source the heavier label wins,
/// a tie going to the programme. A segment more than half outside the guide's programme is never a label.
/// </summary>
public static class SegmentLabels
{
    public const double MostCatalogWeight = 0.9;

    public const double SkippedWeight = 0.2;

    public const double ReturnedWeight = 0.2;

    public const double DismissedWeight = 0.1;

    public const double MostOutsideTheGuide = 0.5;

    public static readonly TimeSpan ReturnedSpan = TimeSpan.FromSeconds(10);

    public static IReadOnlyList<SegmentLabel?> Of(
        IReadOnlyList<SegmentPiece> pieces,
        SegmentContext context,
        IReadOnlyList<ViewerAction> actions)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(actions);

        Stretch[] heard = [.. context.Catalog.Select(Heard).OfType<Stretch>()];
        Stretch[] viewed = Viewed(actions);

        return
        [
            .. pieces.Select(piece => piece.Features[SegmentFeature.OutsideGuide] > MostOutsideTheGuide
                ? null
                : Over(piece, heard, SegmentLabelSource.Catalog) ?? Over(piece, viewed, SegmentLabelSource.Viewer)),
        ];
    }

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
