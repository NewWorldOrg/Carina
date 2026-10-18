namespace Carina.Domain.Segments;

/// <summary>
/// A stretch of sound, by the time it starts and ends.
/// </summary>
public sealed record SoundSpan
{
    public SoundSpan(TimeSpan starts, TimeSpan ends)
    {
        if (ends <= starts)
        {
            throw new ArgumentException("A stretch of sound ends after it starts.", nameof(ends));
        }

        Starts = starts;
        Ends = ends;
    }

    public TimeSpan Starts { get; }

    public TimeSpan Ends { get; }

    public TimeSpan Length => Ends - Starts;

    /// <summary>
    /// Whether the two stretches share at least half of the shorter one.
    /// </summary>
    public bool MostlyShares(SoundSpan other)
    {
        ArgumentNullException.ThrowIfNull(other);

        TimeSpan shared = (Ends < other.Ends ? Ends : other.Ends) - (Starts > other.Starts ? Starts : other.Starts);
        TimeSpan shorter = Length < other.Length ? Length : other.Length;

        return shared * 2 >= shorter;
    }
}
