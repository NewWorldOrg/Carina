using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class AuthSessionService(
    IAuthSessionRepository sessions,
    IPlaybackGrantStore grants,
    SessionPolicy policy,
    TimeProvider clock)
{
    public const string NoSuchSession = "There is no such session.";

    public async Task<ServiceResult<IReadOnlyList<SessionView>>> ListAsync(
        SessionId current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<AuthSession> held = await sessions.ListAllAsync(cancellationToken);

        return ServiceResult<IReadOnlyList<SessionView>>.Success(
        [
            .. held
                .Where(session => session.StatusAt(now, policy) is SessionStatus.Active)
                .Select(session => SessionView.Of(session, current)),
        ]);
    }

    public async Task<ServiceResult> RevokeAsync(SessionId target, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(target);

        AuthSession? held = await sessions.FindAsync(target, cancellationToken);

        if (held is null)
        {
            return ServiceResult.Failure(NoSuchSession);
        }

        if (held.Revoke(clock.GetUtcNow().UtcDateTime))
        {
            await sessions.SaveAsync(held, cancellationToken);
        }

        grants.RevokeEverythingOf(held.Subject);

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> LogOutAsync(
        Subject subject,
        SessionId current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(current);

        await sessions.DeleteAsync(current, cancellationToken);

        grants.RevokeEverythingOf(subject);

        return ServiceResult.Success();
    }
}
