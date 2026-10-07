namespace Carina.Domain.Segments;

/// <summary>
/// The rate a picture stream is placed at, from how long one of its frames lasts: the nearest standard
/// rate when its frame lasts within <see cref="Closeness"/> of that length, and otherwise that length
/// as it is.
/// </summary>
public static class FrameRates
{
    public const double Closeness = 0.01;

    public static readonly IReadOnlyList<(int Numerator, int Denominator)> Standard =
    [
        (24000, 1001),
        (24, 1),
        (25, 1),
        (30000, 1001),
        (30, 1),
        (50, 1),
        (60000, 1001),
        (60, 1),
    ];

    public static readonly TimeSpan Longest = TimeSpan.FromSeconds(1);

    public static FrameClock Clock(TimeSpan frameLength, TimeSpan firstFrameAt)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(frameLength, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(frameLength, Longest);

        (int numerator, int denominator) = Nearest(frameLength);

        return FrameClock.Of(numerator, denominator, firstFrameAt);
    }

    private static (int Numerator, int Denominator) Nearest(TimeSpan frameLength)
    {
        (int Numerator, int Denominator) nearest = Standard.MinBy(rate => Off(rate, frameLength));

        if (Off(nearest, frameLength) <= Closeness)
        {
            return nearest;
        }

        long common = Common(TimeSpan.TicksPerSecond, frameLength.Ticks);

        return ((int)(TimeSpan.TicksPerSecond / common), (int)(frameLength.Ticks / common));
    }

    private static double Off((int Numerator, int Denominator) rate, TimeSpan frameLength)
    {
        double standard = (double)rate.Denominator / rate.Numerator;

        return Math.Abs(frameLength.TotalSeconds - standard) / standard;
    }

    private static long Common(long left, long right)
    {
        while (right is not 0)
        {
            (left, right) = (right, left % right);
        }

        return left;
    }
}
