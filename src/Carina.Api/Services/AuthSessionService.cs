using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class AuthSessionService(
    IAuthSessionRepository sessions,
    SessionPolicy policy,
    TimeProvider clock)
{
    public const string NoSuchSession = "There is no such session on this account.";

    public async Task<ServiceResult<IReadOnlyList<SessionView>>> ListAsync(
        Subject subject,
        SessionId current,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(current);

        DateTime now = clock.GetUtcNow().UtcDateTime;
        IReadOnlyList<AuthSession> held = await sessions.ListAsync(subject, cancellationToken);

        return ServiceResult<IReadOnlyList<SessionView>>.Success(
        [
            .. held
                .Where(session => session.StatusAt(now, policy) is SessionStatus.Active)
                .Select(session => SessionView.Of(session, current)),
        ]);
    }

    public async Task<ServiceResult> RevokeAsync(
        Subject subject,
        SessionId target,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(target);

        AuthSession? held = await sessions.FindAsync(target, cancellationToken);

        if (held is null || !held.Subject.Equals(subject))
        {
            return ServiceResult.Failure(NoSuchSession);
        }

        if (held.Revoke(clock.GetUtcNow().UtcDateTime))
        {
            await sessions.SaveAsync(held, cancellationToken);
        }

        return ServiceResult.Success();
    }

    public async Task<ServiceResult> LogOutAsync(SessionId current, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(current);

        await sessions.DeleteAsync(current, cancellationToken);

        return ServiceResult.Success();
    }
}
