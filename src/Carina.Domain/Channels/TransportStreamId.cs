namespace Carina.Domain.Channels;

public sealed class TransportStreamId : CommonValueObject<int>
{
    public const int MinValue = 0;
    public const int MaxValue = 65535;

    public TransportStreamId(int value)
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
                $"A transport stream id is {MinValue} to {MaxValue}.");
        }

        return value;
    }
}
