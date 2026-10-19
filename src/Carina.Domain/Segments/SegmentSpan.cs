namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of a recording in its own time, what it is taken for and, when it was detected, how likely
/// that is (0 to 1); a stretch the catalog is sure of carries no probability.
/// </summary>
public sealed record SegmentSpan
{
    public SegmentSpan(TimeSpan starts, TimeSpan ends, SegmentKind kind, double? probability)
    {
        if (starts < TimeSpan.Zero || ends <= starts)
        {
            throw new ArgumentException("A stretch starts no earlier than its recording's own zero and ends after it starts.", nameof(ends));
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A stretch is one of the kinds of segment.");
        }

        if (probability is double given && !(given is >= 0 and <= 1))
        {
            throw new ArgumentOutOfRangeException(nameof(probability), probability, "A probability lies from 0 to 1.");
        }

        Starts = starts;
        Ends = ends;
        Kind = kind;
        Probability = probability;
    }

    public TimeSpan Starts { get; }

    public TimeSpan Ends { get; }

    public SegmentKind Kind { get; }

    public double? Probability { get; }

    public TimeSpan Length => Ends - Starts;
}
