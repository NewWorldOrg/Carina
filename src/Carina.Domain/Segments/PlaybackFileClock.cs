using Carina.Domain.Encodings;

namespace Carina.Domain.Segments;

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
