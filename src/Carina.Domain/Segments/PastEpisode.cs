using Carina.Domain.Recordings;

namespace Carina.Domain.Segments;

public sealed record PastEpisode
{
    public static readonly TimeSpan ShortestHeld = BoundaryCandidates.MergedWithin;

    public PastEpisode(RecordingId recording, TimeSpan programmeStarts, TimeSpan? programmeEnds, IReadOnlyList<SegmentSpan> detected)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(detected);

        if (detected.Any(segment => segment is null))
        {
            throw new ArgumentException("Every stretch of an episode is handed over.", nameof(detected));
        }

        Recording = recording;
        ProgrammeStarts = programmeStarts;
        ProgrammeEnds = programmeEnds;
        Segments = [.. detected];
    }

    public RecordingId Recording { get; }

    public TimeSpan ProgrammeStarts { get; }

    public TimeSpan? ProgrammeEnds { get; }

    public IReadOnlyList<SegmentSpan> Segments { get; }

    public PastEpisode Without(IReadOnlyList<SoundSpan> sounds)
    {
        ArgumentNullException.ThrowIfNull(sounds);

        return sounds.Count is 0
            ? this
            : new PastEpisode(Recording, ProgrammeStarts, ProgrammeEnds, [.. Segments.SelectMany(segment => Left(segment, sounds))]);
    }

    public bool HadABreakNear(TimeSpan fromProgrammeStart, TimeSpan within)
        => Had(SegmentKind.Break, ProgrammeStarts + fromProgrammeStart, within);

    public bool HadAnOpeningNear(TimeSpan fromProgrammeStart, TimeSpan within)
        => Had(SegmentKind.Opening, ProgrammeStarts + fromProgrammeStart, within);

    public bool HadAnEndingNear(TimeSpan beforeProgrammeEnd, TimeSpan within)
        => Segments.Count is not 0 && Had(SegmentKind.Ending, (ProgrammeEnds ?? Segments[^1].Ends) - beforeProgrammeEnd, within);

    private static IEnumerable<SegmentSpan> Left(SegmentSpan segment, IReadOnlyList<SoundSpan> sounds)
    {
        TimeSpan from = segment.Starts;

        foreach (SoundSpan sound in sounds.Where(sound => sound.Starts < segment.Ends && sound.Ends > segment.Starts).OrderBy(sound => sound.Starts))
        {
            if (sound.Starts - from >= ShortestHeld)
            {
                yield return new SegmentSpan(from, sound.Starts, segment.Kind, segment.Probability);
            }

            from = sound.Ends > from ? sound.Ends : from;
        }

        if (segment.Ends - from >= ShortestHeld)
        {
            yield return new SegmentSpan(from, segment.Ends, segment.Kind, segment.Probability);
        }
    }

    private bool Had(SegmentKind kind, TimeSpan at, TimeSpan within)
        => Segments.Any(segment => segment.Kind == kind && segment.Length >= ShortestHeld && segment.Starts <= at + within && segment.Ends >= at - within);
}
