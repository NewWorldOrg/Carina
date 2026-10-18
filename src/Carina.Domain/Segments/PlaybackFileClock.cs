using Carina.Domain.Encodings;

namespace Carina.Domain.Segments;

/// <summary>
/// How a stretch in the recording's own time falls on the file playback plays, the one place it is worked out:
/// the recording's own file plays the recording's own time as it is (<see cref="AsItIs"/>), and an artefact plays
/// it less the head the job that made it skipped, for as long as the artefact was measured to last. A stretch
/// reaching back before the file's zero starts at zero and one reaching past its length ends with it; one wholly
/// outside the file is not played.
/// </summary>
public sealed record PlaybackFileClock
{
    private PlaybackFileClock(TimeSpan headSkip, TimeSpan? length)
    {
        HeadSkip = headSkip;
        Length = length;
    }

    public static PlaybackFileClock AsItIs { get; } = new(TimeSpan.Zero, null);

    public TimeSpan HeadSkip { get; }

    public TimeSpan? Length { get; }

    public static PlaybackFileClock Of(EncodeTimeline timeline)
    {
        ArgumentNullException.ThrowIfNull(timeline);

        return new PlaybackFileClock(timeline.HeadSkip, timeline.ArtefactLength);
    }

    public SegmentSpan? Placed(SegmentSpan stretch)
    {
        ArgumentNullException.ThrowIfNull(stretch);

        TimeSpan starts = stretch.Starts - HeadSkip;
        TimeSpan ends = stretch.Ends - HeadSkip;

        if (Length is { } length && ends > length)
        {
            ends = length;
        }

        if (starts < TimeSpan.Zero)
        {
            starts = TimeSpan.Zero;
        }

        return ends > starts ? new SegmentSpan(starts, ends, stretch.Kind, stretch.Probability) : null;
    }
}
