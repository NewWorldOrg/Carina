using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class Subject : CommonValueObject<string>
{
    public const int LongestValue = 255;

    public Subject(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A subject names someone, so it cannot be blank.", nameof(value));
        }

        if (value.Length > LongestValue)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value),
                value.Length,
                $"A subject is at most {LongestValue} characters.");
        }

        if (value != value.Trim())
        {
            throw new ArgumentException(
                "A subject is stored exactly as the identity provider issued it, and padding would make two rows for one person.",
                nameof(value));
        }

        if (value.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A subject reaches logs and screens, so it carries no control characters.",
                nameof(value));
        }

        return value;
    }
}
