namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of a recording, in its own time, that no learning data could be taken from.
/// </summary>
public sealed record LearningDataGap
{
    public LearningDataGap(TimeSpan from, TimeSpan until)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(from, TimeSpan.Zero);

        if (until <= from)
        {
            throw new ArgumentException("A gap ends after it begins.", nameof(until));
        }

        From = from;
        Until = until;
    }

    public TimeSpan From { get; }

    public TimeSpan Until { get; }
}
