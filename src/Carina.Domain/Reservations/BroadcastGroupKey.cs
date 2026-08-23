using Carina.Domain.Base;

namespace Carina.Domain.Reservations;

public sealed class BroadcastGroupKey : CommonValueObject<string>
{
    public const int MaxLength = 128;

    public BroadcastGroupKey(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A broadcast group key is at most {MaxLength} characters, but this one has {value.Length}.",
                nameof(value));
        }

        if (value.Trim().Length != value.Length)
        {
            throw new ArgumentException("A broadcast group key carries no surrounding space.", nameof(value));
        }

        return value;
    }
}
