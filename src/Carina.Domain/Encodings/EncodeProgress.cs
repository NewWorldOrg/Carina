namespace Carina.Domain.Encodings;

/// <summary>
/// Where a running job has got to. The portion done is at most all of it, and the time left is at
/// least none.
/// </summary>
public sealed record EncodeProgress
{
    private EncodeProgress(TimeSpan reached, TimeSpan? whole, double speed, bool ended)
    {
        Reached = reached;
        Whole = whole;
        Speed = speed;
        Ended = ended;
    }

    public TimeSpan Reached { get; }

    public TimeSpan? Whole { get; }

    public double Speed { get; }

    public bool Ended { get; }

    public double? Portion
        => Whole is not { } whole ? null
            : Ended ? 1
            : Math.Clamp(Reached / whole, 0, 1);

    public TimeSpan? Left => Ended ? TimeSpan.Zero : LeftWhileRunning();

    public static EncodeProgress Of(TimeSpan reached, TimeSpan? whole, double speed, bool ended)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(reached, TimeSpan.Zero, nameof(reached));
        ArgumentOutOfRangeException.ThrowIfNegative(speed);

        if (whole is { } length)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(length, TimeSpan.Zero, nameof(whole));
        }

        return new EncodeProgress(reached, whole, speed, ended);
    }

    private TimeSpan? LeftWhileRunning()
    {
        if (Whole is not { } whole || Speed <= 0)
        {
            return null;
        }

        return whole - Reached is { Ticks: > 0 } more ? more / Speed : TimeSpan.Zero;
    }
}
