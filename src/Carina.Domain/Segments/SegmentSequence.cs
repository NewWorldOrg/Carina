namespace Carina.Domain.Segments;

/// <summary>
/// Decides the kind of each segment by dynamic programming over states of kind, how long the run of breaks so
/// far is, and whether the opening and the ending have been used: the run's real length, the sum of its
/// segments' fitted lengths, rounded to whole seconds and taken modulo the grid (<see cref="RunSteps"/> states).
/// Each state keeps the real length of the best run reaching it, so no rounding builds up along a run; an
/// opening or an ending keeps its real length the same way, in a state of its own while it is still too short
/// to leave, so that a likelier one too short to last never takes the place of one that may. The sequence chosen has the greatest sum of the
/// log-probabilities of its kinds and of what its moves cost: leaving a break for anything else after a run
/// whose real length lies further off the grid than the tolerance costs the penalty. The opening and the ending
/// are one each at most, the opening before the ending, and each lasts from
/// <see cref="CatalogKinds.ThemeShortest"/> to <see cref="CatalogKinds.ThemeLongest"/>; a segment of the opening
/// starts in the first half of the programme and one of the ending ends in its second half, by where it lies from
/// the programme's start and end when that is known, unless the catalog fixes it to that kind; neither runs on across a
/// seam (<see cref="SegmentPiece.AfterASeam"/>); a sequence that breaks one of these is not chosen. The sequence goes
/// from the recording's zero to its end through segments and through neighbouring segments read as one
/// (<see cref="JoinedPiece"/>), each such piece taken for one of its kinds in place of the segments it joins: a joined
/// advert counts as one segment, since a pod is cut at every advert and the advert it joins is whole; a joined opening or
/// ending counts as the segments it joins, each at the chance of the whole, since any stretch of a song's length is
/// joined, so that a song is taken whole where the whole is likelier than its parts. Entering a break costs nothing, so a break may start at the recording's zero,
/// and one running on to the end is never left. A tie keeps the programme. Neighbouring segments of one kind
/// are then one stretch, whose probability is the length-weighted mean of its segments' chances of that kind.
/// A break that does not run on to the end, within the tolerance of the grid, is pulled onto it by moving its
/// end, as the rule-based detection did. Boundaries are kept to <see cref="Resolution"/>. A model that tells
/// only breaks from the programme makes no opening or ending, and decides as it did before there were four.
/// <para>
/// A segment the catalog fixes to a kind (<see cref="SegmentPiece.Fixed"/>) is that kind whatever the regression
/// takes it for, at the chance the catalog fixes it at, its entry's confidence, as long as the model tells that kind
/// apart; every model tells the programme. It still sits inside the runs, the
/// costs and the rules above, and the segments beside it are decided by the regression: the sequence chosen
/// overrules the fewest fixed segments any sequence keeping the rules can, and among those has the greatest sum,
/// an overruled segment counting as what the regression takes it for.
/// </para>
/// </summary>
public static class SegmentSequence
{
    public const int RunSteps = 15;

    public static readonly TimeSpan Resolution = TimeSpan.FromMilliseconds(10);

    private const int ShortOpening = 2 * RunSteps;

    private const int OpeningState = ShortOpening + 1;

    private const int ShortEnding = OpeningState + 1;

    private const int EndingState = ShortEnding + 1;

    private const int Block = EndingState + 1;

    private const int OpeningUsed = 1;

    private const int EndingUsed = 2;

    private const int States = (Block * (OpeningUsed | EndingUsed)) + Block;

    private const double HalfTheProgramme = 0.5;

    /// <summary>
    /// Decides the kinds from the scores of a break against the programme alone.
    /// </summary>
    public static SegmentKind[] Decide(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<double> scores, SegmentThresholds thresholds)
        => Decide(pieces, Breaks(scores), thresholds);

    public static SegmentKind[] Decide(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<SegmentChances> chances, SegmentThresholds thresholds)
    {
        Matched(pieces, chances);
        ArgumentNullException.ThrowIfNull(thresholds);

        SegmentKind[] kinds = new SegmentKind[pieces.Count];

        foreach (Step step in Chosen(pieces, chances, [], [], thresholds))
        {
            kinds.AsSpan(step.First, step.Last - step.First + 1).Fill(step.Kind);
        }

        return kinds;
    }

    /// <summary>
    /// The stretches of the kinds decided from the segments and the joined pieces over them: the sequence chosen goes
    /// from the recording's zero to its end through segments and joined pieces, a joined piece taken for one of its kinds
    /// at its own chance of that kind as one segment, in place of the segments it joins.
    /// </summary>
    public static IReadOnlyList<SegmentSpan> Decided(
        IReadOnlyList<SegmentPiece> pieces,
        IReadOnlyList<SegmentChances> chances,
        IReadOnlyList<JoinedPiece> joined,
        IReadOnlyList<SegmentChances> joinedChances,
        TimeSpan length,
        SegmentThresholds thresholds)
    {
        Matched(pieces, chances);
        ArgumentNullException.ThrowIfNull(joined);
        ArgumentNullException.ThrowIfNull(joinedChances);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (joinedChances.Count != joined.Count)
        {
            throw new ArgumentException("Every joined piece is given its chances.", nameof(joinedChances));
        }

        if (joined.Any(piece => piece.Last >= pieces.Count))
        {
            throw new ArgumentException("A joined piece joins segments there are.", nameof(joined));
        }

        return Stretched(Chosen(pieces, chances, joined, joinedChances, thresholds), length, thresholds);
    }

    /// <summary>
    /// The stretches of the kinds decided from the scores of a break against the programme alone.
    /// </summary>
    public static IReadOnlyList<SegmentSpan> Spans(
        IReadOnlyList<SegmentPiece> pieces,
        IReadOnlyList<SegmentKind> kinds,
        IReadOnlyList<double> scores,
        TimeSpan length,
        SegmentThresholds thresholds)
        => Spans(pieces, kinds, Breaks(scores), length, thresholds);

    public static IReadOnlyList<SegmentSpan> Spans(
        IReadOnlyList<SegmentPiece> pieces,
        IReadOnlyList<SegmentKind> kinds,
        IReadOnlyList<SegmentChances> chances,
        TimeSpan length,
        SegmentThresholds thresholds)
    {
        Matched(pieces, chances);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (kinds.Count != pieces.Count)
        {
            throw new ArgumentException("Every piece is given a kind.", nameof(kinds));
        }

        return Stretched([.. pieces.Select((piece, at) => new Step(at, at, new Piece(piece, chances[at]), kinds[at]))], length, thresholds);
    }

    private static List<SegmentSpan> Stretched(IReadOnlyList<Step> steps, TimeSpan length, SegmentThresholds thresholds)
    {
        List<Stretch> stretches = [];

        foreach (Step step in steps)
        {
            SegmentPiece segment = step.Piece.Segment;
            double chance = step.Piece.Chance(step.Kind);
            Stretch piece = new(Rounded(segment.Starts, length), Rounded(segment.Ends, length), step.Kind, chance * segment.Length.TotalSeconds, segment.Length.TotalSeconds);

            if (stretches.Count > 0 && stretches[^1].Kind == step.Kind)
            {
                stretches[^1] = stretches[^1].Joined(piece);
                continue;
            }

            stretches.Add(piece);
        }

        PulledOntoTheGrid(stretches, length, TimeSpan.FromSeconds(thresholds.GridToleranceSec));

        return [.. stretches.Select(stretch => new SegmentSpan(stretch.Starts, stretch.Ends, stretch.Kind, Math.Clamp(stretch.Weighted / stretch.Weight, 0, 1)))];
    }

    /// <summary>
    /// The steps of the sequence chosen, from the recording's zero to its end: each a segment, or a joined piece, and
    /// the kind it is taken for.
    /// </summary>
    private static List<Step> Chosen(
        IReadOnlyList<SegmentPiece> pieces,
        IReadOnlyList<SegmentChances> chances,
        IReadOnlyList<JoinedPiece> joined,
        IReadOnlyList<SegmentChances> joinedChances,
        SegmentThresholds thresholds)
    {
        List<Edge>[] leaving = [.. Enumerable.Range(0, pieces.Count).Select(at => new List<Edge> { new(at, at, new Piece(pieces[at], chances[at]), null) })];

        for (int at = 0; at < joined.Count; at++)
        {
            JoinedPiece piece = joined[at];
            int counts = piece.Kinds.Contains(SegmentKind.Break) ? 1 : piece.Last - piece.First + 1;

            leaving[piece.First].Add(new Edge(piece.First, piece.Last, new Piece(piece.Piece, joinedChances[at], counts), piece.Kinds));
        }

        Column[] columns = [Column.Starting(), .. Enumerable.Range(0, pieces.Count).Select(_ => new Column())];

        for (int node = 0; node < pieces.Count; node++)
        {
            for (int edge = 0; edge < leaving[node].Count; edge++)
            {
                Stepped(columns, node, edge, leaving[node][edge], thresholds);
            }
        }

        return Traced(columns, leaving, Ending(columns[^1]));
    }

    /// <summary>
    /// Offers every move from each state reached at <paramref name="node"/> through <paramref name="step"/>, the
    /// <paramref name="edge"/>th of the steps leaving it, to the node it reaches.
    /// </summary>
    private static void Stepped(Column[] columns, int node, int edge, Edge step, SegmentThresholds thresholds)
    {
        Column from = columns[node];
        Column to = columns[step.Last + 1];

        for (int state = 0; state < States; state++)
        {
            if (from.Reached[state].IsReached)
            {
                Moved(to, new Came(node, state, edge), from.Reached[state], from.Runs[state], step, thresholds);
            }
        }
    }

    private static SegmentChances[] Breaks(IReadOnlyList<double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        return [.. scores.Select(SegmentChances.OfABreak)];
    }

    private static void Matched<T>(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<T> chances)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(chances);

        if (chances.Count != pieces.Count)
        {
            throw new ArgumentException("Every piece is given a score.", nameof(chances));
        }
    }

    /// <summary>
    /// Offers every move from the state <paramref name="came"/> from through <paramref name="step"/>, of the kinds it may
    /// be taken for: to the programme, to a break, to the opening and to the ending, in that order, so that a tie keeps
    /// the earlier.
    /// </summary>
    private static void Moved(Column next, Came came, Reach reach, TimeSpan run, Edge step, SegmentThresholds thresholds)
    {
        int state = came.State;
        SegmentKind kind = KindOf(state);
        int used = state / Block;
        Piece piece = step.Piece;
        TimeSpan length = piece.Segment.Length;

        bool across = piece.Segment.AfterASeam && kind is SegmentKind.Opening or SegmentKind.Ending;

        if (IsShort(state))
        {
            if (!across && step.Takes(kind) && piece.Lies(kind))
            {
                Themed(next, (used * Block) + (kind is SegmentKind.Opening ? ShortOpening : ShortEnding), came, reach.Then(piece.Taken(kind)), run + length);
            }

            return;
        }

        double leaving = Leaving(state, run, thresholds);
        TimeSpan breaks = (kind is SegmentKind.Break ? run : TimeSpan.Zero) + piece.Segment.Fitted;

        if (step.Takes(SegmentKind.Programme))
        {
            next.Offer(used * Block, came, reach.Then(piece.Taken(SegmentKind.Programme), leaving), TimeSpan.Zero);
        }

        if (step.Takes(SegmentKind.Break))
        {
            next.Offer((used * Block) + BreakState(breaks), came, reach.Then(piece.Taken(SegmentKind.Break)), breaks);
        }

        if (((kind is SegmentKind.Opening && !across) || used is 0) && step.Takes(SegmentKind.Opening) && piece.Lies(SegmentKind.Opening))
        {
            Themed(next, (OpeningUsed * Block) + ShortOpening, came, reach.Then(piece.Taken(SegmentKind.Opening), leaving), (kind is SegmentKind.Opening ? run : TimeSpan.Zero) + length);
        }

        if (((kind is SegmentKind.Ending && !across) || (used & EndingUsed) is 0) && step.Takes(SegmentKind.Ending) && piece.Lies(SegmentKind.Ending))
        {
            Themed(next, ((used | EndingUsed) * Block) + ShortEnding, came, reach.Then(piece.Taken(SegmentKind.Ending), leaving), (kind is SegmentKind.Ending ? run : TimeSpan.Zero) + length);
        }
    }

    /// <summary>
    /// Offers a move into the opening or the ending whose state while still too short to leave is
    /// <paramref name="shortState"/>, lasting <paramref name="run"/> with it: to that state while it is still that
    /// short, to the one after it once it may be left, and nowhere when it is longer than one may last.
    /// </summary>
    private static void Themed(Column next, int shortState, Came came, Reach reach, TimeSpan run)
    {
        if (run <= CatalogKinds.ThemeLongest)
        {
            next.Offer(run < CatalogKinds.ThemeShortest ? shortState : shortState + 1, came, reach, run);
        }
    }

    /// <summary>
    /// The state the sequence chosen ends in: the best one may end in, the earliest of those that tie.
    /// </summary>
    private static int Ending(Column last)
    {
        int ending = 0;

        for (int state = 1; state < States; state++)
        {
            if (MayEnd(state) && last.Reached[state].Beats(last.Reached[ending]))
            {
                ending = state;
            }
        }

        return ending;
    }

    private static bool IsShort(int state) => state % Block is ShortOpening or ShortEnding;

    private static bool MayEnd(int state) => !IsShort(state);

    private static SegmentKind KindOf(int state)
    {
        int local = state % Block;

        return local switch
        {
            ShortOpening or OpeningState => SegmentKind.Opening,
            ShortEnding or EndingState => SegmentKind.Ending,
            >= RunSteps => SegmentKind.Break,
            _ => SegmentKind.Programme,
        };
    }

    private static int BreakState(TimeSpan run) => RunSteps + (int)(Math.Round(run.TotalSeconds, MidpointRounding.AwayFromZero) % RunSteps);

    private static double Leaving(int state, TimeSpan run, SegmentThresholds thresholds)
        => KindOf(state) is SegmentKind.Break && SegmentGrid.Off(run) > TimeSpan.FromSeconds(thresholds.GridToleranceSec) ? thresholds.GridPenalty : 0;

    private static List<Step> Traced(Column[] columns, List<Edge>[] leaving, int last)
    {
        List<Step> steps = [];
        int node = columns.Length - 1;
        int state = last;

        while (node > 0)
        {
            Came came = columns[node].Came[state];
            Edge edge = leaving[came.Node][came.Edge];

            steps.Add(new Step(edge.First, edge.Last, edge.Piece, KindOf(state)));
            node = came.Node;
            state = came.State;
        }

        steps.Reverse();

        return steps;
    }

    private static TimeSpan Rounded(TimeSpan at, TimeSpan length)
        => at <= TimeSpan.Zero || at >= length
            ? at
            : TimeSpan.FromTicks((long)Math.Round(at.Ticks / (double)Resolution.Ticks, MidpointRounding.AwayFromZero) * Resolution.Ticks);

    private static void PulledOntoTheGrid(List<Stretch> stretches, TimeSpan length, TimeSpan tolerance)
    {
        for (int at = 0; at + 1 < stretches.Count; at++)
        {
            Stretch gap = stretches[at];
            TimeSpan off = SegmentGrid.Off(gap.Ends - gap.Starts);
            TimeSpan pulled = gap.Starts + (SegmentGrid.Step * Math.Round((gap.Ends - gap.Starts) / SegmentGrid.Step, MidpointRounding.AwayFromZero));

            if (gap.Kind is SegmentKind.Break && off <= tolerance && pulled > gap.Starts && pulled < stretches[at + 1].Ends && pulled <= length)
            {
                stretches[at] = gap with { Ends = pulled };
                stretches[at + 1] = stretches[at + 1] with { Starts = pulled };
            }
        }
    }

    /// <summary>
    /// How a state is reached: by overruling how many fixed segments, and with what sum. Fewer overruled beats
    /// more, then the greater sum; a sum of nothing at all is not reached.
    /// </summary>
    private readonly record struct Reach(int Overruled, double Score)
    {
        public static Reach None { get; } = new(int.MaxValue, double.NegativeInfinity);

        public bool IsReached => !double.IsNegativeInfinity(Score);

        public Reach Then(Reach taken, double cost = 0) => new(Overruled + taken.Overruled, Score + cost + taken.Score);

        public bool Beats(Reach other) => Overruled != other.Overruled ? Overruled < other.Overruled : Score > other.Score;
    }

    /// <summary>
    /// One segment with its chances, fixed when the catalog fixes it to a kind the model tells apart, counting as
    /// <see cref="Counts"/> segments.
    /// </summary>
    private readonly record struct Piece(SegmentPiece Segment, SegmentChances Chances, int Counts = 1)
    {
        public CatalogFix? Fixed => Segment.Fixed is { } fix && Chances.Weighs(fix.Kind) ? fix : null;

        /// <summary>
        /// Whether this segment lies where <paramref name="kind"/> may: an opening starts in the first half of the
        /// programme and an ending ends in its second half; anywhere when where it lies is not known, or when the
        /// catalog fixes it to that kind.
        /// </summary>
        public bool Lies(SegmentKind kind)
            => Fixed?.Kind == kind || kind switch
            {
                SegmentKind.Opening => !(Segment.Features[SegmentFeature.FromStart] >= HalfTheProgramme),
                SegmentKind.Ending => !(Segment.Features[SegmentFeature.FromEnd] >= HalfTheProgramme),
                _ => true,
            };

        public Reach Taken(SegmentKind kind)
        {
            if (Fixed is not { } fix)
            {
                return new Reach(0, Counts * Chances.LogOf(kind));
            }

            return fix.Kind == kind ? new Reach(0, Math.Log(fix.Chance)) : new Reach(1, Chances.LogOf(kind));
        }

        public double Chance(SegmentKind kind) => Fixed is { } fix && fix.Kind == kind ? fix.Chance : Chances.Of(kind);
    }

    /// <summary>
    /// Where a state was reached from: the node before the step, the state there, and which of the steps leaving that
    /// node it took.
    /// </summary>
    private readonly record struct Came(int Node, int State, int Edge);

    /// <summary>
    /// A step from one node to another: a segment, or a joined piece over the segments from <see cref="First"/> to
    /// <see cref="Last"/>, which may be taken for any kind when <see cref="Kinds"/> is not given.
    /// </summary>
    private readonly record struct Edge(int First, int Last, Piece Piece, IReadOnlyList<SegmentKind>? Kinds)
    {
        public bool Takes(SegmentKind kind) => Kinds is null || Kinds.Contains(kind);
    }

    private readonly record struct Step(int First, int Last, Piece Piece, SegmentKind Kind);

    private sealed class Column
    {
        public Reach[] Reached { get; } = [.. Enumerable.Repeat(Reach.None, States)];

        public TimeSpan[] Runs { get; } = new TimeSpan[States];

        public Came[] Came { get; } = new Came[States];

        public static Column Starting()
        {
            Column start = new();

            start.Reached[0] = new Reach(0, 0);

            return start;
        }

        public void Offer(int to, Came from, Reach reach, TimeSpan run)
        {
            if (reach.IsReached && reach.Beats(Reached[to]))
            {
                Reached[to] = reach;
                Runs[to] = run;
                Came[to] = from;
            }
        }
    }

    private readonly record struct Stretch(TimeSpan Starts, TimeSpan Ends, SegmentKind Kind, double Weighted, double Weight)
    {
        public Stretch Joined(Stretch next) => this with { Ends = next.Ends, Weighted = Weighted + next.Weighted, Weight = Weight + next.Weight };
    }
}
