namespace Carina.Domain.Encodings;

/// <summary>
/// Where a running job has got to. The portion cannot be more than all of it and nothing left can
/// be less than none, because both are worked out here rather than by whoever draws them.
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

    public TimeSpan? Left
        => Ended ? TimeSpan.Zero
            : Whole is not { } whole || Speed <= 0 ? null
            : whole - Reached is { Ticks: > 0 } more ? more / Speed
            : TimeSpan.Zero;

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
}
