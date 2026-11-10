using Carina.Domain.Base;

namespace Carina.Domain.Notifications;

public sealed class NotificationName : CommonValueObject<string>
{
    public const int Longest = 64;

    public NotificationName(string value)
        : base(Validated(value))
    {
    }

    /// <summary>
    /// The name as written, or nothing where it is not one a destination or a set of rules can carry.
    /// </summary>
    public static NotificationName? Read(string? written)
    {
        string trimmed = written?.Trim() ?? string.Empty;

        return trimmed.Length is 0 or > Longest || trimmed.Any(char.IsControl) ? null : new NotificationName(trimmed);
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
