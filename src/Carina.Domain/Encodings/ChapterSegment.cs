namespace Carina.Domain.Encodings;

public enum ChapterKind
{
    Programme = 1,

    Break = 2,
}

/// <summary>
/// One stretch of an artefact, on the artefact's own clock, and what a run took it to be. A
/// detection lays these end to end over the whole length with no gap between them, so a position
/// falls in exactly one of them.
/// </summary>
public sealed record ChapterSegment
{
    public ChapterSegment(TimeSpan starts, TimeSpan ends, ChapterKind kind)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(starts, TimeSpan.Zero, nameof(starts));

        if (ends <= starts)
        {
            throw new ArgumentOutOfRangeException(nameof(ends), ends, "A chapter ends after it starts.");
        }

        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind), kind, "A chapter is one of the kinds named here.");
        }

        Starts = starts;
        Ends = ends;
        Kind = kind;
    }

    public TimeSpan Starts { get; }

    public TimeSpan Ends { get; }

    public ChapterKind Kind { get; }

    public TimeSpan Length => Ends - Starts;
}
