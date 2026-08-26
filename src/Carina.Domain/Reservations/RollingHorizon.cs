using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed class RollingHorizon : CommonValueObject<TimeSpan>
{
    public static readonly TimeSpan Provisional = TimeSpan.FromMinutes(30);

    public RollingHorizon(TimeSpan value)
        : base(Validated(value))
    {
    }

    public static RollingHorizon Default { get; } = new(Provisional);

    private static TimeSpan Validated(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                "A horizon is how far ahead a recording with no announced end keeps its tuner, which is longer than no time at all.");
        }

        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException("A horizon is a whole number of seconds.", nameof(value));
        }

        return value;
    }
}
