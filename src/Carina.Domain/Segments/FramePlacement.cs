namespace Carina.Domain.Segments;

/// <summary>
/// Where a frame goes: whether it is kept, how many copies of the frame before it are put in ahead
/// of it, and the gap those copies stand for when they last long enough to be one.
/// </summary>
public readonly record struct FramePlace(bool Kept, long Repeats, LearningDataGap? Gap);

/// <summary>
/// Places frames handed over with their times on the frames of <see cref="Clock"/>: each goes to the
/// frame nearest its time. A frame that lands where one is already placed, or before it, is dropped.
/// One that lands further on is kept after copies of the frame before it, filling the frames it
/// skipped, and the frames it skipped are a gap when they last at least
/// <see cref="LearningDataGaps.Shortest"/>.
/// </summary>
public sealed class FramePlacement(FrameClock clock)
{
    public FrameClock Clock { get; } = clock ?? throw new ArgumentNullException(nameof(clock));

    public long Placed { get; private set; }

    public TimeSpan PlacedThrough => Clock.At(Placed);

    public FramePlace Place(TimeSpan at)
    {
        long frame = Nearest(at);

        if (frame < Placed)
        {
            return new FramePlace(false, 0, null);
        }

        long repeats = frame - Placed;
        LearningDataGap? gap = Clock.At(frame) - Clock.At(Placed) >= LearningDataGaps.Shortest
            ? new LearningDataGap(Clock.At(Placed), Clock.At(frame))
            : null;

        Placed = frame + 1;

        return new FramePlace(true, repeats, gap);
    }

    private long Nearest(TimeSpan at)
    {
        Int128 scaled = (Int128)(at - Clock.FirstFrameAt).Ticks * Clock.Numerator;
        Int128 frameTicks = (Int128)Clock.Denominator * TimeSpan.TicksPerSecond;
        Int128 half = frameTicks / 2;

        return (long)((scaled >= 0 ? scaled + half : scaled - half) / frameTicks);
    }
}
