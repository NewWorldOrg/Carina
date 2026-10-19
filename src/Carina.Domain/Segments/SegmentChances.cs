namespace Carina.Domain.Segments;

/// <summary>
/// How likely one segment is to be each kind, by the softmax of the scores of the kinds a model weighs against
/// the programme, which scores nothing; a kind the model does not weigh is not likely at all. With the break
/// alone weighed, the chance of a break is the logistic function of its score, to the last bit, and the
/// programme has the rest.
/// </summary>
public sealed class SegmentChances
{
    private static readonly SegmentKind[] Every = Enum.GetValues<SegmentKind>();

    private readonly double[] scores;

    private readonly double highest;

    private readonly double sum;

    private SegmentChances(double[] scores)
    {
        this.scores = scores;
        highest = 0;

        foreach (double score in scores)
        {
            if (score > highest)
            {
                highest = score;
            }
        }

        sum = 0;

        foreach (double score in scores)
        {
            sum += Math.Exp(score - highest);
        }
    }

    public static SegmentChances Of(IReadOnlyDictionary<SegmentKind, double> scores)
    {
        ArgumentNullException.ThrowIfNull(scores);

        double[] held = [.. Every.Select(kind => kind is SegmentKind.Programme ? 0 : double.NegativeInfinity)];

        foreach ((SegmentKind kind, double score) in scores)
        {
            if (kind is SegmentKind.Programme || !Enum.IsDefined(kind))
            {
                throw new ArgumentException("The kinds scored are weighed against the programme, which scores nothing.", nameof(scores));
            }

            if (!double.IsFinite(score))
            {
                throw new ArgumentOutOfRangeException(nameof(scores), score, "A score is a finite number.");
            }

            held[At(kind)] = score;
        }

        return new SegmentChances(held);
    }

    public static SegmentChances OfABreak(double score) => Of(new Dictionary<SegmentKind, double> { [SegmentKind.Break] = score });

    /// <summary>
    /// The chance of <paramref name="kind"/>; the programme's is what the others leave.
    /// </summary>
    public double Of(SegmentKind kind)
    {
        if (kind is SegmentKind.Programme)
        {
            return Math.Max(0, 1 - (Of(SegmentKind.Break) + Of(SegmentKind.Opening) + Of(SegmentKind.Ending)));
        }

        return Math.Exp(scores[At(kind)] - highest) / sum;
    }

    public double LogOf(SegmentKind kind) => (scores[At(kind)] - highest) - Math.Log(sum);

    private static int At(SegmentKind kind) => (int)kind - (int)SegmentKind.Programme;
}
