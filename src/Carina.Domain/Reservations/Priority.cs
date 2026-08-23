using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed class Priority : CommonValueObject<int>
{
    public const int MinValue = 1;

    public const int MaxValue = 99;

    public const int DefaultValue = 10;

    public Priority(int value)
        : base(Validated(value))
    {
    }

    public static Priority Default { get; } = new(DefaultValue);

    private static int Validated(int value)
    {
        if (value is < MinValue or > MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"A priority is {MinValue} to {MaxValue}.");
        }

        return value;
    }
}
