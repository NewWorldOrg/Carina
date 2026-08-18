using Carina.Domain.Base;

namespace Carina.Domain.Programmes;

public sealed class EventId : CommonValueObject<int>
{
    public const int MinValue = 1;

    public const int MaxValue = 65534;

    public EventId(int value)
        : base(Validated(value))
    {
    }

    private static int Validated(int value)
    {
        if (value is < MinValue or > MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"An event id is {MinValue} to {MaxValue}.");
        }

        return value;
    }
}
