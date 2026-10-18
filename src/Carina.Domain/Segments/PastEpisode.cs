using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

/// <summary>
/// An earlier episode of the series of the recording judged: which recording it is, where its programme starts
/// and ends in its own recording's time, and the stretches it was taken to hold, by the answer a person gave
/// for it when there is one and otherwise by its detection. An episode whose programme end the guide did not
/// give ends where its stretches do.
/// </summary>
public sealed record PastEpisode
{
    public PastEpisode(
        RecordingId recording,
        TimeSpan programmeStarts,
        TimeSpan? programmeEnds,
        IReadOnlyList<SegmentSpan>? detected,
        IReadOnlyList<SegmentSpan>? answered)
    {
        ArgumentNullException.ThrowIfNull(recording);

        if (detected is null && answered is null)
        {
            throw new ArgumentException("An episode is remembered by its detection, the answer given for it, or both.", nameof(answered));
        }

        if ((detected ?? []).Concat(answered ?? []).Any(segment => segment is null))
        {
            throw new ArgumentException("Every stretch of an episode is handed over.", nameof(detected));
        }

        Recording = recording;
        ProgrammeStarts = programmeStarts;
        ProgrammeEnds = programmeEnds;
        Detected = detected is null ? null : [.. detected];
        Answered = answered is null ? null : [.. answered];
    }

    public RecordingId Recording { get; }

    public TimeSpan ProgrammeStarts { get; }

    public TimeSpan? ProgrammeEnds { get; }

    public IReadOnlyList<SegmentSpan>? Detected { get; }

    public IReadOnlyList<SegmentSpan>? Answered { get; }

    public IReadOnlyList<SegmentSpan> Segments => Answered ?? Detected ?? [];

    public bool StandsOnTheAnswer => Answered is not null;

    /// <summary>
    /// The episode as its detection alone has it; none when it was not detected.
    /// </summary>
    public PastEpisode? WithoutTheAnswer()
        => Detected is null ? null : new PastEpisode(Recording, ProgrammeStarts, ProgrammeEnds, Detected, null);

    /// <summary>
    /// Whether a break of this episode lies within <paramref name="within"/> of the same distance from
    /// its programme's start as <paramref name="fromProgrammeStart"/>.
    /// </summary>
    public bool HadABreakNear(TimeSpan fromProgrammeStart, TimeSpan within)
        => Had(SegmentKind.Break, ProgrammeStarts + fromProgrammeStart, within);

    /// <summary>
    /// Whether an opening of this episode lies within <paramref name="within"/> of the same distance from
    /// its programme's start as <paramref name="fromProgrammeStart"/>.
    /// </summary>
    public bool HadAnOpeningNear(TimeSpan fromProgrammeStart, TimeSpan within)
        => Had(SegmentKind.Opening, ProgrammeStarts + fromProgrammeStart, within);

    /// <summary>
    /// Whether an ending of this episode lies within <paramref name="within"/> of the same distance before
    /// its programme's end as <paramref name="beforeProgrammeEnd"/>.
    /// </summary>
    public bool HadAnEndingNear(TimeSpan beforeProgrammeEnd, TimeSpan within)
        => Segments.Count is not 0 && Had(SegmentKind.Ending, (ProgrammeEnds ?? Segments[^1].Ends) - beforeProgrammeEnd, within);

    private bool Had(SegmentKind kind, TimeSpan at, TimeSpan within)
        => Segments.Any(segment => segment.Kind == kind && segment.Starts <= at + within && segment.Ends >= at - within);
}
