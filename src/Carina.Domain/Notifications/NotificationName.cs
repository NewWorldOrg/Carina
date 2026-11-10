using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class NotificationName : CommonValueObject<string>
{
    public const int Longest = 64;

    public NotificationName(string value)
        : base(Validated(value))
    {
    }

    private static string Validated(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        string trimmed = value.Trim();

        if (trimmed.Length > Longest)
        {
            throw new ArgumentException(
                $"A name is at most {Longest} characters, but this one has {trimmed.Length}.",
                nameof(value));
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException("A name is read by a person, so it carries no control character.", nameof(value));
        }

        return trimmed;
    }
}
