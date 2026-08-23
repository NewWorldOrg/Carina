using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed class Margin : CommonValueObject<TimeSpan>
{
    public static readonly TimeSpan Longest = TimeSpan.FromHours(1);

    public Margin(TimeSpan value)
        : base(Validated(value))
    {
    }

    public static Margin None { get; } = new(TimeSpan.Zero);

    public int Seconds => (int)Value.TotalSeconds;

    public static Margin OfSeconds(int seconds) => new(TimeSpan.FromSeconds(seconds));

    private static TimeSpan Validated(TimeSpan value)
    {
        if (value < TimeSpan.Zero || value > Longest)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A margin is zero to {Longest}.");
        }

        if (value.Ticks % TimeSpan.TicksPerSecond != 0)
        {
            throw new ArgumentException("A margin is a whole number of seconds.", nameof(value));
        }

        return value;
    }
}
