using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class LocalAccountService(
    ILocalAccountRepository accounts,
    IAuthSessionRepository sessions,
    IPasswordHasher hasher,
    ILoginThrottle throttle,
    PasswordHashPolicy hashPolicy,
    SessionPolicy sessionPolicy,
    TimeProvider clock)
{
    public const string TheSameRefusalForEveryBadLogin = "The username or the password is wrong.";

    public const string TheRefusalForTooManyAttempts =
        "Too many sign-in attempts came from here. Wait for the window to pass and try again.";

    public const int ShortestPassword = 12;

    public const int LongestPassword = 256;

    private readonly PasswordHash decoy = PasswordHash.Encode(
        hashPolicy,
        new byte[hashPolicy.SaltLength],
        new byte[hashPolicy.DigestLength]);

    public async Task<ServiceResult<LoginOutcome>> LogInAsync(
        LoginAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (throttle.RefusesUntil(attempt.Caller) is { } until)
        {
            return ServiceResult<LoginOutcome>.Success(
                LoginOutcome.HeldOff(until, sessionPolicy.AbsoluteLifetime));
        }

        LocalAccount? account = await accounts.FindAsync(cancellationToken);

        if (!Admits(account, attempt.Username, attempt.Password))
        {
            throttle.Failed(attempt.Caller);

            return ServiceResult<LoginOutcome>.Success(LoginOutcome.Refused(sessionPolicy.AbsoluteLifetime));
        }

        throttle.Passed(attempt.Caller);

        AuthSession session = AuthSession.Start(
            SessionId.Issue(),
            new Subject(account!.Username),
            AuthMethod.Local,
            attempt.DeviceLabel,
            clock.GetUtcNow().UtcDateTime);

        await sessions.SaveAsync(session, cancellationToken);

        return ServiceResult<LoginOutcome>.Success(
            LoginOutcome.Started(session, sessionPolicy.AbsoluteLifetime));
    }

    public async Task<ServiceResult<int, PasswordRefusal>> ChangePasswordAsync(
        PasswordChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        LocalAccount? account = await accounts.FindAsync(cancellationToken);

        if (account is null
            || string.IsNullOrEmpty(change.Current)
            || !hasher.Matches(change.Current, account.PasswordHash))
        {
            return ServiceResult<int, PasswordRefusal>.Failure(
                "The current password is wrong.",
                PasswordRefusal.WrongPassword);
        }

        if (change.Replacement.Length < ShortestPassword || change.Replacement.Length > LongestPassword)
        {
            return ServiceResult<int, PasswordRefusal>.Failure(
                $"A password is between {ShortestPassword} and {LongestPassword} characters long.",
                PasswordRefusal.TooWeak);
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;

        account.ChangePassword(hasher.Hash(change.Replacement, hashPolicy), at);

        await accounts.SaveAsync(account, cancellationToken);

        return ServiceResult<int, PasswordRefusal>.Success(
            await EndEveryOtherSessionAsync(change, at, cancellationToken));
    }

    private async Task<int> EndEveryOtherSessionAsync(
        PasswordChange change,
        DateTime at,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<AuthSession> held = await sessions.ListAsync(change.Subject, cancellationToken);
        var ended = new List<AuthSession>();

        foreach (AuthSession session in held)
        {
            if (!session.Id.Equals(change.Keep) && session.Revoke(at))
            {
                ended.Add(session);
            }
        }

        if (ended.Count > 0)
        {
            await sessions.SaveAllAsync(ended, cancellationToken);
        }

        return ended.Count;
    }

    private bool Admits(LocalAccount? account, string username, string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        bool digestsAgree = hasher.Matches(password, account?.PasswordHash ?? decoy);

        return account is not null
            && digestsAgree
            && string.Equals(account.Username, username, StringComparison.Ordinal);
    }
}
