using Carina.Domain.Base;

namespace Carina.Domain.Channels;

public sealed class LogoId : CommonValueObject<int>
{
    public const int MinValue = 0;

    public const int MaxValue = 511;

    public LogoId(int value)
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
                $"A logo id is {MinValue} to {MaxValue}.");
        }

        return value;
    }
}
