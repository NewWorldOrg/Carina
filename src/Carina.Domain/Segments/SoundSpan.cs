namespace Carina.Domain.Segments;

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

    public bool MostlyShares(SoundSpan other)
    {
        ArgumentNullException.ThrowIfNull(other);

        TimeSpan shared = (Ends < other.Ends ? Ends : other.Ends) - (Starts > other.Starts ? Starts : other.Starts);
        TimeSpan shorter = Length < other.Length ? Length : other.Length;

        return shared * 2 >= shorter;
    }
}
