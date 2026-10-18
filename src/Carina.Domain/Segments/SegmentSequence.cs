namespace Carina.Domain.Segments;

/// <summary>
/// Decides the kind of each segment by dynamic programming over states of kind and how long the run of
/// breaks so far is: its real length, the sum of its segments' fitted lengths, rounded to whole seconds
/// and taken modulo the grid (<see cref="RunSteps"/> states). Each state keeps the real length of the
/// best run reaching it, so no rounding builds up along a run. The sequence chosen has the greatest sum
/// of the log-probabilities of its kinds and of what its moves cost: leaving a break for the programme
/// after a run whose real length lies further off the grid than the tolerance costs the penalty.
/// Entering a break costs nothing, so a break may start at the recording's zero, and one running on to
/// the end is never left. A tie keeps the programme. Neighbouring segments of one kind are then one
/// stretch, whose probability is the length-weighted mean of its segments'. A break that does not run on
/// to the end, within the tolerance of the grid, is pulled onto it by moving its end, as the rule-based
/// detection did. Boundaries are kept to <see cref="Resolution"/>.
/// </summary>
public static class SegmentSequence
{
    public const int RunSteps = 15;

    public static readonly TimeSpan Resolution = TimeSpan.FromMilliseconds(10);

    private const int States = 2 * RunSteps;

    public static SegmentKind[] Decide(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<double> scores, SegmentThresholds thresholds)
    {
        Matched(pieces, scores);
        ArgumentNullException.ThrowIfNull(thresholds);

        Column best = Column.Starting();
        int[][] came = new int[pieces.Count][];

        for (int at = 0; at < pieces.Count; at++)
        {
            Column next = new();

            for (int state = 0; state < States; state++)
            {
                if (double.IsNegativeInfinity(best.Scores[state]))
                {
                    continue;
                }

                TimeSpan run = (state >= RunSteps ? best.Runs[state] : TimeSpan.Zero) + pieces[at].Fitted;

                next.Offer(0, state, best.Scores[state] + Leaving(state, best.Runs[state], thresholds) + LogChance(-scores[at]), TimeSpan.Zero);
                next.Offer(BreakState(run), state, best.Scores[state] + LogChance(scores[at]), run);
            }

            came[at] = next.Came;
            best = next;
        }

        return Traced(came, Array.IndexOf(best.Scores, best.Scores.Max()));
    }

    public static IReadOnlyList<SegmentSpan> Spans(
        IReadOnlyList<SegmentPiece> pieces,
        IReadOnlyList<SegmentKind> kinds,
        IReadOnlyList<double> scores,
        TimeSpan length,
        SegmentThresholds thresholds)
    {
        Matched(pieces, scores);
        ArgumentNullException.ThrowIfNull(kinds);
        ArgumentNullException.ThrowIfNull(thresholds);

        if (kinds.Count != pieces.Count)
        {
            throw new ArgumentException("Every piece is given a kind.", nameof(kinds));
        }

        List<Stretch> stretches = [];

        for (int at = 0; at < pieces.Count; at++)
        {
            double chance = kinds[at] is SegmentKind.Break ? SegmentModel.Chance(scores[at]) : 1 - SegmentModel.Chance(scores[at]);
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

    private static void Matched(IReadOnlyList<SegmentPiece> pieces, IReadOnlyList<double> scores)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(scores);

        if (scores.Count != pieces.Count)
        {
            throw new ArgumentException("Every piece is given a score.", nameof(scores));
        }
    }

    private static int BreakState(TimeSpan run) => RunSteps + (int)(Math.Round(run.TotalSeconds, MidpointRounding.AwayFromZero) % RunSteps);

    private static double Leaving(int state, TimeSpan run, SegmentThresholds thresholds)
        => state >= RunSteps && SegmentGrid.Off(run) > TimeSpan.FromSeconds(thresholds.GridToleranceSec) ? thresholds.GridPenalty : 0;

    private static double LogChance(double score) => score >= 0 ? -Math.Log(1 + Math.Exp(-score)) : score - Math.Log(1 + Math.Exp(score));


    private static SegmentKind[] Traced(int[][] came, int last)
    {
        SegmentKind[] kinds = new SegmentKind[came.Length];
        int state = last;

        for (int at = came.Length - 1; at >= 0; at--)
        {
            kinds[at] = state >= RunSteps ? SegmentKind.Break : SegmentKind.Programme;
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
