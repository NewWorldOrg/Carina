namespace Carina.Domain.Segments;

/// <summary>
/// Where the frames of a picture stream fall on the recording's own time: frame <c>n</c> is
/// <c>n</c> frames of <see cref="Denominator"/> / <see cref="Numerator"/> seconds after
/// <see cref="FirstFrameAt"/>. A clock faster than <see cref="FastestFrames"/> frames a second, or
/// whose first frame comes before the recording's time zero, is refused.
/// </summary>
public sealed record FrameClock
{
    public const int FastestFrames = 240;

    private FrameClock(int numerator, int denominator, TimeSpan firstFrameAt)
    {
        Numerator = numerator;
        Denominator = denominator;
        FirstFrameAt = firstFrameAt;
    }

    public int Numerator { get; }

    public int Denominator { get; }

    public TimeSpan FirstFrameAt { get; }

    public static FrameClock Of(int numerator, int denominator, TimeSpan firstFrameAt)
    {
        string? fault = Fault(numerator, denominator, firstFrameAt);

        return fault is null ? new FrameClock(numerator, denominator, firstFrameAt) : throw new ArgumentException(fault);
    }

    public TimeSpan At(long frame)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frame);

        return FirstFrameAt + TimeSpan.FromTicks((long)((Int128)frame * Denominator * TimeSpan.TicksPerSecond / Numerator));
    }

    public long FirstFrameFrom(TimeSpan time)
    {
        if (time <= FirstFrameAt)
        {
            return 0;
        }

        Int128 scaled = (Int128)(time - FirstFrameAt).Ticks * Numerator;
        Int128 frameTicks = (Int128)Denominator * TimeSpan.TicksPerSecond;

        return (long)((scaled + frameTicks - 1) / frameTicks);
    }

    public FrameClock From(TimeSpan time) => new(Numerator, Denominator, At(FirstFrameFrom(time)));

    public int MostFramesIn(int seconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(seconds);

        return (int)((((long)seconds * Numerator) + Denominator - 1) / Denominator);
    }

    internal static string? Fault(int numerator, int denominator, TimeSpan firstFrameAt)
    {
        if (numerator < 1 || denominator < 1)
        {
            return "A frame rate is a ratio of two counts, each at least one.";
        }

        if (numerator > (long)denominator * FastestFrames)
        {
            return $"No picture stream runs faster than {FastestFrames} frames a second.";
        }

        return firstFrameAt < TimeSpan.Zero ? "The first frame comes no earlier than the recording's time zero." : null;
    }
}
