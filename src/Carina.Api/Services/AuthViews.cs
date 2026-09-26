using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed record LoginAttempt(string Username, string Password, string DeviceLabel, string Caller);

public sealed record LoginOutcome(AuthSession? Session, SessionId? Cookie, DateTime? RetryAt, TimeSpan SessionLifetime)
{
    public static LoginOutcome Started(SessionId cookie, AuthSession session, TimeSpan lifetime)
        => new(session, cookie, null, lifetime);

    public static LoginOutcome Refused(TimeSpan lifetime) => new(null, null, null, lifetime);

    public static LoginOutcome HeldOff(DateTime until, TimeSpan lifetime) => new(null, null, until, lifetime);
}

public sealed record PasswordChange(Subject Subject, SessionHandle Keep, string Current, string Replacement);

public enum PasswordRefusal
{
    None,
    WrongPassword,
    TooWeak,
}

public sealed record SessionView(
    SessionHandle Handle,
    string DisplayName,
    AuthMethod Method,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    string DeviceLabel,
    bool Current)
{
    public static SessionView Of(AuthSession session, SessionHandle current)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(current);

        return new SessionView(
            session.Handle,
            session.DisplayName,
            session.Method,
            session.CreatedAt,
            session.LastUsedAt,
            session.DeviceLabel,
            session.Handle.Equals(current));
    }
}
