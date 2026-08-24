using Carina.Domain.Base;

namespace Carina.Domain.Recordings;

public sealed class TunerDeviceId : CommonValueObject<string>
{
    public const int MaxLength = 64;

    public TunerDeviceId(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        if (value.Length > MaxLength)
        {
            throw new ArgumentException(
                $"A tuner device id is at most {MaxLength} characters, but this one has {value.Length}.",
                nameof(value));
        }

        foreach (char letter in value)
        {
            bool allowed = letter is >= 'a' and <= 'z'
                or >= 'A' and <= 'Z'
                or >= '0' and <= '9'
                or '-'
                or '_'
                or '.';

            if (!allowed)
            {
                throw new ArgumentException(
                    $"A tuner device id is a name the driver detected, so '{letter}' has no place in one.",
                    nameof(value));
            }
        }

        return value;
    }
}
