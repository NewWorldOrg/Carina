namespace Carina.Domain.Segments;

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

    private static void Themed(Column next, int shortState, Came came, Reach reach, TimeSpan run)
    {
        if (run <= CatalogKinds.ThemeLongest)
        {
            next.Offer(run < CatalogKinds.ThemeShortest ? shortState : shortState + 1, came, reach, run);
        }
    }

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

    private readonly record struct Reach(int Overruled, double Score)
    {
        public static Reach None { get; } = new(int.MaxValue, double.NegativeInfinity);

        public bool IsReached => !double.IsNegativeInfinity(Score);

        public Reach Then(Reach taken, double cost = 0) => new(Overruled + taken.Overruled, Score + cost + taken.Score);

        public bool Beats(Reach other) => Overruled != other.Overruled ? Overruled < other.Overruled : Score > other.Score;
    }

    private readonly record struct Piece(SegmentPiece Segment, SegmentChances Chances, int Counts = 1)
    {
        public CatalogFix? Fixed => Segment.Fixed is { } fix && Chances.Weighs(fix.Kind) ? fix : null;

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

    private readonly record struct Came(int Node, int State, int Edge);

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
