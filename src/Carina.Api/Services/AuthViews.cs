using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed record LoginAttempt(string Username, string Password, string DeviceLabel, string Caller);

public sealed record LoginOutcome(AuthSession? Session, DateTime? RetryAt, TimeSpan SessionLifetime)
{
    public static LoginOutcome Started(AuthSession session, TimeSpan lifetime) => new(session, null, lifetime);

    public static LoginOutcome Refused(TimeSpan lifetime) => new(null, null, lifetime);

    public static LoginOutcome HeldOff(DateTime until, TimeSpan lifetime) => new(null, until, lifetime);
}

public sealed record PasswordChange(Subject Subject, SessionId Keep, string Current, string Replacement);

public enum PasswordRefusal
{
    None,
    WrongPassword,
    TooWeak,
}

public sealed record SessionView(
    SessionId Id,
    string DisplayName,
    AuthMethod Method,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    string DeviceLabel,
    bool Current)
{
    public static SessionView Of(AuthSession session, SessionId current)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(current);

        return new SessionView(
            session.Id,
            session.DisplayName,
            session.Method,
            session.CreatedAt,
            session.LastUsedAt,
            session.DeviceLabel,
            session.Id.Equals(current));
    }
}
