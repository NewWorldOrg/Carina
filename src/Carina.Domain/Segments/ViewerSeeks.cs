namespace Carina.Domain.Segments;

/// <summary>
/// What one forward seek passed, read to learn from: from where to where once its ends were drawn to boundary
/// candidates, the weight it labels with, the kind the version in use found over more than half of it, if one other
/// than the programme, and whether it runs from cut to cut in steps as long as adverts.
/// </summary>
public sealed record SeekSpan(TimeSpan Starts, TimeSpan Ends, double Weight, SegmentKind? Detected, bool InAdvertSteps);

/// <summary>
/// The seeks of one recording read to learn from. Nobody lands a seek on a cut, so each end of a forward seek is taken
/// to lie within <see cref="Reach"/> of where it was sent, by how it was made, and is drawn to the likeliest boundary
/// candidate within that reach: first one that faces a pod, as long as an advert from another candidate on the side of
/// what was passed and not from one on the other side, then one as long as an advert from a candidate on the side of what
/// was passed whatever lies on the other, then the stronger, then the nearer. An end with no candidate within reach is drawn in by the
/// reach, so that nothing near it is labelled, and each such end lightens the seek from <see cref="MostWeight"/> to
/// <see cref="OneEndLeftWeight"/> and <see cref="BothEndsLeftWeight"/>. A seek that keeps less than
/// <see cref="ShortestSpan"/> is not used; a seek the viewer came back from, the latest earlier forward seek whose
/// stretch holds where they came back to, is not used either, and where they came back to is kept. Coming back to within
/// the reach of where that seek landed is putting right a seek that went a little too far, which leaves the seek as it
/// is and keeps nothing. The same stretch passed more than once is one.
/// </summary>
public sealed class ViewerSeeks
{
    public const double MostWeight = 0.1;

    public const double OneEndLeftWeight = 0.07;

    public const double BothEndsLeftWeight = 0.05;

    public static readonly TimeSpan ShortestSpan = TimeSpan.FromSeconds(10);

    private ViewerSeeks(IReadOnlyList<SeekSpan> spans, IReadOnlyList<TimeSpan> cameBackTo)
    {
        Spans = spans;
        CameBackTo = cameBackTo;
    }

    public static ViewerSeeks None { get; } = new([], []);

    public IReadOnlyList<SeekSpan> Spans { get; }

    public IReadOnlyList<TimeSpan> CameBackTo { get; }

    /// <summary>
    /// How far either way of where it was sent an end of a seek made by <paramref name="means"/> may lie.
    /// </summary>
    public static TimeSpan Reach(SeekMeans means) => means switch
    {
        SeekMeans.Bar or SeekMeans.Key => TimeSpan.FromSeconds(8),
        SeekMeans.Step => TimeSpan.FromSeconds(10),
        SeekMeans.Chapter => TimeSpan.FromSeconds(2),
        _ => throw new ArgumentOutOfRangeException(nameof(means), means, "A seek is made by the bar, a step, a chapter or a key."),
    };

    /// <summary>
    /// <paramref name="seeks"/> read against the boundary candidates of the recording, the version in use's detection
    /// of it, if there is one, and the tolerance of the grid adverts are laid on.
    /// </summary>
    public static ViewerSeeks Read(
        IReadOnlyList<ViewerSeek> seeks,
        IReadOnlyList<BoundaryCandidate> candidates,
        SegmentDetection? inUse,
        TimeSpan tolerance)
    {
        ArgumentNullException.ThrowIfNull(seeks);
        ArgumentNullException.ThrowIfNull(candidates);

        if (seeks.Count is 0)
        {
            return None;
        }

        ViewerSeek[] ordered = [.. seeks.OrderBy(seek => seek.HappenedAt)];
        ViewerSeek[] returned = [.. ordered.Where(seek => seek.Action is SeekAction.Returned)];
        HashSet<ViewerSeek> cameBackFrom = new(ReferenceEqualityComparer.Instance);
        List<TimeSpan> cameBackTo = [];

        foreach (ViewerSeek back in returned)
        {
            ViewerSeek? passed = ordered.LastOrDefault(seek => seek.Action is SeekAction.Forward
                                                               && seek.HappenedAt <= back.HappenedAt
                                                               && seek.From <= back.To
                                                               && back.To < seek.To
                                                               && !cameBackFrom.Contains(seek));

            if (passed is not null && back.To >= passed.To - Reach(passed.Means))
            {
                continue;
            }

            if (passed is not null)
            {
                cameBackFrom.Add(passed);
            }

            cameBackTo.Add(back.To);
        }

        Reading reading = new([.. candidates.OrderBy(candidate => candidate.At)], inUse, tolerance);
        SeekSpan[] spans =
        [
            .. ordered
                .Where(seek => seek.Action is SeekAction.Forward && !cameBackFrom.Contains(seek))
                .Select(reading.Span)
                .OfType<SeekSpan>()
                .GroupBy(span => (span.Starts, span.Ends))
                .Select(same => same.MaxBy(span => span.Weight)!),
        ];

        return new ViewerSeeks(spans, cameBackTo);
    }

    /// <summary>
    /// The kind <paramref name="span"/> labels with: the kind the stretches of <paramref name="catalog"/> cover more than
    /// half of it with, none when that is the programme, since the catalog wins; else the kind the version in use found;
    /// else a break when it runs from cut to cut in steps as long as adverts; else none.
    /// </summary>
    public static SegmentKind? KindOf(SeekSpan span, IReadOnlyList<CatalogHeard> catalog)
    {
        ArgumentNullException.ThrowIfNull(span);
        ArgumentNullException.ThrowIfNull(catalog);

        SegmentKind? heard = MostOver(span.Starts, span.Ends, catalog.Where(stretch => stretch.Kind is not null).Select(stretch => (stretch.Starts, stretch.Ends, stretch.Kind!.Value)));

        if (heard is not null)
        {
            return heard is SegmentKind.Programme ? null : heard;
        }

        return span.Detected ?? (span.InAdvertSteps ? SegmentKind.Break : null);
    }

    private static SegmentKind? MostOver(TimeSpan starts, TimeSpan ends, IEnumerable<(TimeSpan Starts, TimeSpan Ends, SegmentKind Kind)> stretches)
        => stretches
            .Where(stretch => stretch.Starts < ends && stretch.Ends > starts)
            .GroupBy(stretch => stretch.Kind)
            .Select(kind => (Kind: kind.Key, Covered: Covered(starts, ends, kind.Select(stretch => (stretch.Starts, stretch.Ends)))))
            .Where(kind => kind.Covered * 2 > ends - starts)
            .OrderByDescending(kind => kind.Covered)
            .Select(kind => (SegmentKind?)kind.Kind)
            .FirstOrDefault();

    private static TimeSpan Covered(TimeSpan starts, TimeSpan ends, IEnumerable<(TimeSpan Starts, TimeSpan Ends)> stretches)
    {
        TimeSpan covered = TimeSpan.Zero;
        TimeSpan reached = starts;

        foreach ((TimeSpan from, TimeSpan to) in stretches.OrderBy(stretch => stretch.Starts))
        {
            TimeSpan begins = from > reached ? from : reached;
            TimeSpan stops = to < ends ? to : ends;

            if (stops > begins)
            {
                covered += stops - begins;
                reached = stops;
            }
        }

        return covered;
    }

    private sealed class Reading(BoundaryCandidate[] candidates, SegmentDetection? inUse, TimeSpan tolerance)
    {
        public SeekSpan? Span(ViewerSeek seek)
        {
            TimeSpan reach = Reach(seek.Means);
            BoundaryCandidate? head = Drawn(seek.From, reach, inwardIsLater: true);
            BoundaryCandidate? tail = Drawn(seek.To, reach, inwardIsLater: false);
            TimeSpan starts = head?.At ?? seek.From + reach;
            TimeSpan ends = tail?.At ?? seek.To - reach;

            if (ends - starts < ShortestSpan)
            {
                return null;
            }

            int left = (head is null ? 1 : 0) + (tail is null ? 1 : 0);
            double weight = left switch
            {
                0 => MostWeight,
                1 => OneEndLeftWeight,
                _ => BothEndsLeftWeight,
            };

            return new SeekSpan(starts, ends, weight, Detected(starts, ends), head is not null && tail is not null && InAdvertSteps(head, tail));
        }

        private BoundaryCandidate? Drawn(TimeSpan at, TimeSpan reach, bool inwardIsLater)
            => candidates
                .Where(candidate => (candidate.At - at).Duration() <= reach)
                .OrderByDescending(candidate => FacingAPod(candidate, inwardIsLater))
                .ThenByDescending(candidate => candidate.Strength)
                .ThenBy(candidate => (candidate.At - at).Duration())
                .ThenBy(candidate => candidate.At)
                .FirstOrDefault();

        /// <summary>
        /// 2 when <paramref name="candidate"/> is as long as an advert from another on the side of what was passed and
        /// from none on the other side, as the edge of a pod is; 1 when it is so only on the side of what was passed;
        /// 0 otherwise.
        /// </summary>
        private int FacingAPod(BoundaryCandidate candidate, bool inwardIsLater)
        {
            bool faces = candidates.Any(other => inwardIsLater ? AnAdvert(candidate, other) : AnAdvert(other, candidate));

            if (!faces)
            {
                return 0;
            }

            return candidates.Any(other => inwardIsLater ? AnAdvert(other, candidate) : AnAdvert(candidate, other)) ? 1 : 2;
        }

        private bool InAdvertSteps(BoundaryCandidate head, BoundaryCandidate tail)
        {
            BoundaryCandidate[] between = [.. candidates.Where(candidate => candidate.At >= head.At && candidate.At <= tail.At)];
            bool[] reached = new bool[between.Length];

            for (int at = 0; at < between.Length; at++)
            {
                reached[at] = at is 0 || Enumerable.Range(0, at).Any(from => reached[from] && AnAdvert(between[from], between[at]));
            }

            return between.Length > 1 && reached[^1];
        }

        private bool AnAdvert(BoundaryCandidate head, BoundaryCandidate tail)
        {
            if (tail.At <= head.At)
            {
                return false;
            }

            TimeSpan shortest = tail.Earliest - head.Latest > TimeSpan.Zero ? tail.Earliest - head.Latest : TimeSpan.Zero;

            return SegmentGrid.IsAsLongAsAnAdvert(shortest, tail.Latest - head.Earliest, tolerance);
        }

        private SegmentKind? Detected(TimeSpan starts, TimeSpan ends)
            => inUse is null || inUse.State is SegmentDetectionState.Discarded
                ? null
                : MostOver(starts, ends, inUse.Segments.Where(segment => segment.Kind is not SegmentKind.Programme).Select(segment => (segment.Starts, segment.Ends, segment.Kind)));
    }
}
