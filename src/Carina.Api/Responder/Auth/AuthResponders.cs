using Carina.Api.Services;
using Carina.Domain.Auth;

namespace Carina.Api.Responder.Auth;

public sealed record MeResponder(string Subject, AuthMethod Method)
{
    public static MeResponder Of(AuthSession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        return new MeResponder(session.Subject.Value, session.Method);
    }
}

public sealed record SessionResponder(
    string Id,
    AuthMethod Method,
    DateTime CreatedAt,
    DateTime LastUsedAt,
    string DeviceLabel,
    bool Current)
{
    public static SessionResponder Of(SessionView view)
    {
        ArgumentNullException.ThrowIfNull(view);

        return new SessionResponder(
            view.Id.Value,
            view.Method,
            view.CreatedAt,
            view.LastUsedAt,
            view.DeviceLabel,
            view.Current);
    }
}

public sealed record PasswordChangedResponder(int SessionsEnded);
