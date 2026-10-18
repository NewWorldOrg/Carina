namespace Carina.Domain.Segments;

/// <summary>
/// Decides the kind of each segment by dynamic programming over states of kind, how long the run of breaks so
/// far is, and whether the opening and the ending have been used: the run's real length, the sum of its
/// segments' fitted lengths, rounded to whole seconds and taken modulo the grid (<see cref="RunSteps"/> states).
/// Each state keeps the real length of the best run reaching it, so no rounding builds up along a run; an
/// opening or an ending keeps its real length the same way. The sequence chosen has the greatest sum of the
/// log-probabilities of its kinds and of what its moves cost: leaving a break for anything else after a run
/// whose real length lies further off the grid than the tolerance costs the penalty. The opening and the ending
/// are one each at most, the opening before the ending, and each lasts from
/// <see cref="CatalogKinds.ThemeShortest"/> to <see cref="CatalogKinds.ThemeLongest"/>; a sequence that breaks
/// one of these is not chosen. Entering a break costs nothing, so a break may start at the recording's zero,
/// and one running on to the end is never left. A tie keeps the programme. Neighbouring segments of one kind
/// are then one stretch, whose probability is the length-weighted mean of its segments' chances of that kind.
/// A break that does not run on to the end, within the tolerance of the grid, is pulled onto it by moving its
/// end, as the rule-based detection did. Boundaries are kept to <see cref="Resolution"/>. A model that tells
/// only breaks from the programme makes no opening or ending, and decides as it did before there were four.
/// </summary>
public static class SegmentSequence
{
    public const int RunSteps = 15;

    public static readonly TimeSpan Resolution = TimeSpan.FromMilliseconds(10);

    private const int OpeningState = 2 * RunSteps;

    private const int EndingState = OpeningState + 1;

    private const int Block = EndingState + 1;

    private const int OpeningUsed = 1;

    private const int EndingUsed = 2;

    private const int States = (Block * (OpeningUsed | EndingUsed)) + Block;

    /// <summary>
    /// Decides the kinds from the scores of a break against the programme alone.
    /// </summary>
    public static SegmentKind[] Decide(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<double> scores, SegmentThresholds thresholds)
        => Decide(pieces, Breaks(scores), thresholds);

    public static SegmentKind[] Decide(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<SegmentChances> chances, SegmentThresholds thresholds)
    {
        Matched(pieces, chances);
        ArgumentNullException.ThrowIfNull(thresholds);

        Column best = Column.Starting();
        int[][] came = new int[pieces.Count][];

        for (int at = 0; at < pieces.Count; at++)
        {
            Column next = new();

            for (int state = 0; state < States; state++)
            {
                if (!double.IsNegativeInfinity(best.Scores[state]))
                {
                    Moved(next, state, best.Scores[state], best.Runs[state], pieces[at], chances[at], thresholds);
                }
            }

            came[at] = next.Came;
            best = next;
        }

        for (int state = 0; state < States; state++)
        {
            if (!MayEnd(state, best.Runs[state]))
            {
                best.Scores[state] = double.NegativeInfinity;
            }
        }

        return Traced(came, Array.IndexOf(best.Scores, best.Scores.Max()));
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

        List<Stretch> stretches = [];

        for (int at = 0; at < pieces.Count; at++)
        {
            double chance = chances[at].Of(kinds[at]);
            Stretch piece = new(Rounded(pieces[at].Starts, length), Rounded(pieces[at].Ends, length), kinds[at], chance * pieces[at].Length.TotalSeconds, pieces[at].Length.TotalSeconds);

            if (stretches.Count > 0 && stretches[^1].Kind == kinds[at])
            {
                stretches[^1] = stretches[^1].Joined(piece);
                continue;
            }

            stretches.Add(piece);
        }

        PulledOntoTheGrid(stretches, length, TimeSpan.FromSeconds(thresholds.GridToleranceSec));

        return [.. stretches.Select(stretch => new SegmentSpan(stretch.Starts, stretch.Ends, stretch.Kind, Math.Clamp(stretch.Weighted / stretch.Weight, 0, 1)))];
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
    /// Offers every move from <paramref name="state"/> through <paramref name="piece"/>: to the programme, to a
    /// break, to the opening and to the ending, in that order, so that a tie keeps the earlier.
    /// </summary>
    private static void Moved(Column next, int state, double score, TimeSpan run, SegmentPiece piece, SegmentChances chances, SegmentThresholds thresholds)
    {
        SegmentKind kind = KindOf(state);
        int used = state / Block;

        if (kind is SegmentKind.Opening or SegmentKind.Ending && run < CatalogKinds.ThemeShortest)
        {
            Stayed(next, state, score, run, piece, chances, kind);

            return;
        }

        double leaving = Leaving(state, run, thresholds);
        TimeSpan breaks = (kind is SegmentKind.Break ? run : TimeSpan.Zero) + piece.Fitted;

        next.Offer(used * Block, state, score + leaving + chances.LogOf(SegmentKind.Programme), TimeSpan.Zero);
        next.Offer((used * Block) + BreakState(breaks), state, score + chances.LogOf(SegmentKind.Break), breaks);

        if (kind is SegmentKind.Opening || used is 0)
        {
            Themed(next, (OpeningUsed * Block) + OpeningState, state, score + leaving + chances.LogOf(SegmentKind.Opening), (kind is SegmentKind.Opening ? run : TimeSpan.Zero) + piece.Length);
        }

        if (kind is SegmentKind.Ending || (used & EndingUsed) is 0)
        {
            Themed(next, ((used | EndingUsed) * Block) + EndingState, state, score + leaving + chances.LogOf(SegmentKind.Ending), (kind is SegmentKind.Ending ? run : TimeSpan.Zero) + piece.Length);
        }
    }

    /// <summary>
    /// The only move from an opening or an ending still too short to leave: on in it.
    /// </summary>
    private static void Stayed(Column next, int state, double score, TimeSpan run, SegmentPiece piece, SegmentChances chances, SegmentKind kind)
        => Themed(next, state, state, score + chances.LogOf(kind), run + piece.Length);

    /// <summary>
    /// Offers a move into an opening or an ending that lasts <paramref name="run"/> with it, when that is no
    /// longer than one may last.
    /// </summary>
    private static void Themed(Column next, int to, int from, double score, TimeSpan run)
    {
        if (run <= CatalogKinds.ThemeLongest)
        {
            next.Offer(to, from, score, run);
        }
    }

    private static bool MayEnd(int state, TimeSpan run)
        => KindOf(state) is not (SegmentKind.Opening or SegmentKind.Ending) || run >= CatalogKinds.ThemeShortest;

    private static SegmentKind KindOf(int state)
    {
        int local = state % Block;

        return local switch
        {
            OpeningState => SegmentKind.Opening,
            EndingState => SegmentKind.Ending,
            >= RunSteps => SegmentKind.Break,
            _ => SegmentKind.Programme,
        };
    }

    private static int BreakState(TimeSpan run) => RunSteps + (int)(Math.Round(run.TotalSeconds, MidpointRounding.AwayFromZero) % RunSteps);

    private static double Leaving(int state, TimeSpan run, SegmentThresholds thresholds)
        => KindOf(state) is SegmentKind.Break && SegmentGrid.Off(run) > TimeSpan.FromSeconds(thresholds.GridToleranceSec) ? thresholds.GridPenalty : 0;

    private static SegmentKind[] Traced(int[][] came, int last)
    {
        SegmentKind[] kinds = new SegmentKind[came.Length];
        int state = last;

        for (int at = came.Length - 1; at >= 0; at--)
        {
            kinds[at] = KindOf(state);
            state = came[at][state];
        }

        return kinds;
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

    private sealed class Column
    {
        public double[] Scores { get; } = [.. Enumerable.Repeat(double.NegativeInfinity, States)];

        public TimeSpan[] Runs { get; } = new TimeSpan[States];

        public int[] Came { get; } = new int[States];

        public static Column Starting()
        {
            Column start = new();

            start.Scores[0] = 0;

            return start;
        }

        public void Offer(int to, int from, double score, TimeSpan run)
        {
            if (score > Scores[to])
            {
                Scores[to] = score;
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
