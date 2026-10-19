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
/// <see cref="CatalogKinds.ThemeShortest"/> to <see cref="CatalogKinds.ThemeLongest"/>; a sequence that breaks
/// one of these is not chosen. Entering a break costs nothing, so a break may start at the recording's zero,
/// and one running on to the end is never left. A tie keeps the programme. Neighbouring segments of one kind
/// are then one stretch, whose probability is the length-weighted mean of its segments' chances of that kind.
/// A break that does not run on to the end, within the tolerance of the grid, is pulled onto it by moving its
/// end, as the rule-based detection did. Boundaries are kept to <see cref="Resolution"/>. A model that tells
/// only breaks from the programme makes no opening or ending, and decides as it did before there were four.
/// <para>
/// A segment the catalog fixes to a kind (<see cref="SegmentPiece.Fixed"/>) is that kind whatever the regression
/// takes it for, with the chance of <see cref="FixedBreakChance"/> for a break and <see cref="FixedThemeChance"/>
/// for an opening or an ending, as long as the model tells that kind apart. It still sits inside the runs, the
/// costs and the rules above, and the segments beside it are decided by the regression: the sequence chosen
/// overrules the fewest fixed segments any sequence keeping the rules can, and among those has the greatest sum,
/// an overruled segment counting as what the regression takes it for.
/// </para>
/// </summary>
public static class SegmentSequence
{
    public const int RunSteps = 15;

    public const double FixedBreakChance = 0.9;

    public const double FixedThemeChance = 0.8;

    public static readonly TimeSpan Resolution = TimeSpan.FromMilliseconds(10);

    private const int ShortOpening = 2 * RunSteps;

    private const int OpeningState = ShortOpening + 1;

    private const int ShortEnding = OpeningState + 1;

    private const int EndingState = ShortEnding + 1;

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
            Piece piece = new(pieces[at], chances[at]);

            for (int state = 0; state < States; state++)
            {
                if (best.Reached[state].IsReached)
                {
                    Moved(next, state, best.Reached[state], best.Runs[state], piece, thresholds);
                }
            }

            came[at] = next.Came;
            best = next;
        }

        return Traced(came, Ending(best));
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
            double chance = new Piece(pieces[at], chances[at]).Chance(kinds[at]);
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
    private static void Moved(Column next, int state, Reach reach, TimeSpan run, Piece piece, SegmentThresholds thresholds)
    {
        SegmentKind kind = KindOf(state);
        int used = state / Block;
        TimeSpan length = piece.Segment.Length;

        if (IsShort(state))
        {
            Themed(next, (used * Block) + (kind is SegmentKind.Opening ? ShortOpening : ShortEnding), state, reach.Then(piece.Taken(kind)), run + length);

            return;
        }

        double leaving = Leaving(state, run, thresholds);
        TimeSpan breaks = (kind is SegmentKind.Break ? run : TimeSpan.Zero) + piece.Segment.Fitted;

        next.Offer(used * Block, state, reach.Then(piece.Taken(SegmentKind.Programme), leaving), TimeSpan.Zero);
        next.Offer((used * Block) + BreakState(breaks), state, reach.Then(piece.Taken(SegmentKind.Break)), breaks);

        if (kind is SegmentKind.Opening || used is 0)
        {
            Themed(next, (OpeningUsed * Block) + ShortOpening, state, reach.Then(piece.Taken(SegmentKind.Opening), leaving), (kind is SegmentKind.Opening ? run : TimeSpan.Zero) + length);
        }

        if (kind is SegmentKind.Ending || (used & EndingUsed) is 0)
        {
            Themed(next, ((used | EndingUsed) * Block) + ShortEnding, state, reach.Then(piece.Taken(SegmentKind.Ending), leaving), (kind is SegmentKind.Ending ? run : TimeSpan.Zero) + length);
        }
    }

    /// <summary>
    /// Offers a move into the opening or the ending whose state while still too short to leave is
    /// <paramref name="shortState"/>, lasting <paramref name="run"/> with it: to that state while it is still that
    /// short, to the one after it once it may be left, and nowhere when it is longer than one may last.
    /// </summary>
    private static void Themed(Column next, int shortState, int from, Reach reach, TimeSpan run)
    {
        if (run <= CatalogKinds.ThemeLongest)
        {
            next.Offer(run < CatalogKinds.ThemeShortest ? shortState : shortState + 1, from, reach, run);
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

    /// <summary>
    /// How a state is reached: by overruling how many fixed segments, and with what sum. Fewer overruled beats
    /// more, then the greater sum; a sum of nothing at all is not reached.
    /// </summary>
    private readonly record struct Reach(int Overruled, double Score)
    {
        public static Reach None { get; } = new(int.MaxValue, double.NegativeInfinity);

        public bool IsReached => !double.IsNegativeInfinity(Score);

        public Reach Then(Reach taken, double cost = 0) => new(Overruled + taken.Overruled, Score + taken.Score + cost);

        public bool Beats(Reach other) => Overruled != other.Overruled ? Overruled < other.Overruled : Score > other.Score;
    }

    /// <summary>
    /// One segment with its chances, fixed to a kind when the catalog fixes it to a break, an opening or an ending
    /// the model tells apart.
    /// </summary>
    private readonly record struct Piece(SegmentPiece Segment, SegmentChances Chances)
    {
        public SegmentKind? Fixed
            => Segment.Fixed is SegmentKind kind and not SegmentKind.Programme && Chances.Weighs(kind) ? kind : null;

        public Reach Taken(SegmentKind kind)
        {
            if (Fixed is not SegmentKind fixedTo)
            {
                return new Reach(0, Chances.LogOf(kind));
            }

            return fixedTo == kind ? new Reach(0, Math.Log(FixedChance(kind))) : new Reach(1, Chances.LogOf(kind));
        }

        public double Chance(SegmentKind kind) => Fixed == kind ? FixedChance(kind) : Chances.Of(kind);

        private static double FixedChance(SegmentKind kind) => kind is SegmentKind.Break ? FixedBreakChance : FixedThemeChance;
    }

    private sealed class Column
    {
        public Reach[] Reached { get; } = [.. Enumerable.Repeat(Reach.None, States)];

        public TimeSpan[] Runs { get; } = new TimeSpan[States];

        public int[] Came { get; } = new int[States];

        public static Column Starting()
        {
            Column start = new();

            start.Reached[0] = new Reach(0, 0);

            return start;
        }

        public void Offer(int to, int from, Reach reach, TimeSpan run)
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
