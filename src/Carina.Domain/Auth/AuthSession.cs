using Carina.Domain.Base;

namespace Carina.Domain.Auth;

public enum AuthMethod
{
    Local = 1,

    Oidc = 2,
}

public enum SessionStatus
{
    Active = 1,

    Expired = 2,

    Revoked = 3,
}

public sealed class AuthSession
{
    public const int LongestDeviceLabel = 120;

    public const int LongestDisplayName = 255;

    private AuthSession()
    {
    }

    public SessionId Id { get; private set; } = null!;

    public Subject Subject { get; private set; } = null!;

    public string DisplayName { get; private set; } = null!;

    public AuthMethod Method { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime LastUsedAt { get; private set; }

    public string DeviceLabel { get; private set; } = null!;

    public DateTime? RevokedAt { get; private set; }

    public static AuthSession Start(
        SessionId id,
        Subject subject,
        string displayName,
        AuthMethod method,
        string deviceLabel,
        DateTime at)
        => Rehydrate(id, subject, displayName, method, at, at, deviceLabel, null);

    public static AuthSession Rehydrate(
        SessionId id,
        Subject subject,
        string displayName,
        AuthMethod method,
        DateTime createdAt,
        DateTime lastUsedAt,
        string deviceLabel,
        DateTime? revokedAt)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(subject);

        DateTime created = UtcTimes.Required(createdAt, nameof(createdAt));
        DateTime lastUsed = UtcTimes.Required(lastUsedAt, nameof(lastUsedAt));
        DateTime? revoked = UtcTimes.Optional(revokedAt, nameof(revokedAt));

        ArgumentOutOfRangeException.ThrowIfLessThan(lastUsed, created, nameof(lastUsedAt));

        if (revoked is not null)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(revoked.Value, created, nameof(revokedAt));
        }

        return new AuthSession
        {
            Id = id,
            Subject = subject,
            DisplayName = ValidatedDisplayName(displayName),
            Method = method,
            CreatedAt = created,
            LastUsedAt = lastUsed,
            DeviceLabel = ValidatedDeviceLabel(deviceLabel),
            RevokedAt = revoked,
        };
    }

    public SessionStatus StatusAt(DateTime at, SessionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        UtcTimes.Required(at, nameof(at));

        if (RevokedAt is not null)
        {
            return SessionStatus.Revoked;
        }

        if (at >= CreatedAt + policy.AbsoluteLifetime || at >= LastUsedAt + policy.IdleTimeout)
        {
            return SessionStatus.Expired;
        }

        return SessionStatus.Active;
    }

    public bool Touch(DateTime at, SessionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        UtcTimes.Required(at, nameof(at));

        if (RevokedAt is not null || at - LastUsedAt < policy.BetweenLastUsedWrites)
        {
            return false;
        }

        LastUsedAt = at;

        return true;
    }

    public bool Revoke(DateTime at)
    {
        UtcTimes.Required(at, nameof(at));

        if (RevokedAt is not null)
        {
            return false;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(at, CreatedAt, nameof(at));
        RevokedAt = at;

        return true;
    }

    private static string ValidatedDisplayName(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);

        string trimmed = displayName.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "A session is listed beside everyone else's, so it says whose it is.",
                nameof(displayName));
        }

        if (trimmed.Length > LongestDisplayName)
        {
            throw new ArgumentOutOfRangeException(
                nameof(displayName),
                trimmed.Length,
                $"A display name is at most {LongestDisplayName} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A display name is issued by an identity provider and reaches screens, so it carries no control characters.",
                nameof(displayName));
        }

        return trimmed;
    }

    private static string ValidatedDeviceLabel(string deviceLabel)
    {
        ArgumentNullException.ThrowIfNull(deviceLabel);

        string trimmed = deviceLabel.Trim();

        if (trimmed.Length == 0)
        {
            throw new ArgumentException(
                "A session is listed by the device it belongs to, so it carries a label.",
                nameof(deviceLabel));
        }

        if (trimmed.Length > LongestDeviceLabel)
        {
            throw new ArgumentOutOfRangeException(
                nameof(deviceLabel),
                trimmed.Length,
                $"A device label is at most {LongestDeviceLabel} characters.");
        }

        if (trimmed.Any(char.IsControl))
        {
            throw new ArgumentException(
                "A device label is built from a user agent the caller chose, so it carries no control characters.",
                nameof(deviceLabel));
        }

        return trimmed;
    }
}
