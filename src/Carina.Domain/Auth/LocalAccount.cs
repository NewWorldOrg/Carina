using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public sealed class LocalAccount
{
    public const int TheOnlyRow = 1;

    public const int LongestUsername = 64;

    private LocalAccount()
    {
    }

    public int Id { get; private set; }

    public string Username { get; private set; } = null!;

    public PasswordHash PasswordHash { get; private set; } = null!;

    public DateTime CreatedAt { get; private set; }

    public DateTime PasswordChangedAt { get; private set; }

    public static LocalAccount Bootstrap(string username, PasswordHash passwordHash, DateTime at)
        => Rehydrate(TheOnlyRow, username, passwordHash, at, at);

    public static LocalAccount Rehydrate(
        int id,
        string username,
        PasswordHash passwordHash,
        DateTime createdAt,
        DateTime passwordChangedAt)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);

        DateTime created = UtcTimes.Required(createdAt, nameof(createdAt));
        DateTime changed = UtcTimes.Required(passwordChangedAt, nameof(passwordChangedAt));

        ArgumentOutOfRangeException.ThrowIfLessThan(changed, created, nameof(passwordChangedAt));

        return new LocalAccount
        {
            Id = id,
            Username = ValidatedUsername(username),
            PasswordHash = passwordHash,
            CreatedAt = created,
            PasswordChangedAt = changed,
        };
    }

    public void ChangePassword(PasswordHash passwordHash, DateTime at)
    {
        ArgumentNullException.ThrowIfNull(passwordHash);
        UtcTimes.Required(at, nameof(at));
        ArgumentOutOfRangeException.ThrowIfLessThan(at, CreatedAt, nameof(at));

        PasswordHash = passwordHash;
        PasswordChangedAt = at;
    }

    private static string ValidatedUsername(string username)
    {
        ArgumentNullException.ThrowIfNull(username);

        string trimmed = username.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "The local account is signed in to by name, so it has one.",
                nameof(username));
        }

        if (trimmed.Length > LongestUsername)
        {
            throw new ArgumentOutOfRangeException(
                nameof(username),
                trimmed.Length,
                $"A username is at most {LongestUsername} characters.");
        }

        if (trimmed.Any(character => char.IsWhiteSpace(character) || char.IsControl(character)))
        {
            throw new ArgumentException(
                "A username is typed back into a login form, so it holds no whitespace.",
                nameof(username));
        }

        return trimmed;
    }
}
