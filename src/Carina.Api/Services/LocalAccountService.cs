using Carina.Api.Common;
using Carina.Domain.Auth;

namespace Carina.Api.Services;

public sealed class LocalAccountService(
    ILocalAccountRepository accounts,
    IAuthSessionRepository sessions,
    PasswordChangeDomainService passwords,
    IPlaybackGrantStore grants,
    IHlsKeyRevocation playlists,
    IPasswordHasher hasher,
    ILoginThrottle throttle,
    PasswordHashPolicy hashPolicy,
    SessionPolicy sessionPolicy,
    TimeProvider clock)
{
    public const string TheSameRefusalForEveryBadLogin = "The username or the password is wrong.";

    public const string TheRefusalForTooManyAttempts =
        "Too many sign-in attempts came from here. Wait for the window to pass and try again.";

    private readonly PasswordHash decoy = PasswordHash.Encode(
        hashPolicy,
        new byte[hashPolicy.SaltLength],
        new byte[hashPolicy.DigestLength]);

    public async Task<ServiceResult<LoginOutcome>> LogInAsync(
        LoginAttempt attempt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attempt);

        if (throttle.TakeTry(attempt.Caller) is { } until)
        {
            return ServiceResult<LoginOutcome>.Success(
                LoginOutcome.HeldOff(until, sessionPolicy.AbsoluteLifetime));
        }

        LocalAccount? account = await accounts.FindAsync(cancellationToken);

        if (!Admits(account, attempt.Username, attempt.Password))
        {
            return ServiceResult<LoginOutcome>.Success(LoginOutcome.Refused(sessionPolicy.AbsoluteLifetime));
        }

        throttle.Passed(attempt.Caller);

        SessionId cookie = SessionId.Issue();
        AuthSession session = AuthSession.Start(
            cookie,
            new Subject(account!.Username),
            account.Username,
            AuthMethod.Local,
            attempt.DeviceLabel,
            clock.GetUtcNow().UtcDateTime);

        await sessions.SaveAsync(session, cancellationToken);

        await RemakeTheStoredHashIfItIsWeakerThanThePolicyAsync(account, attempt.Password, cancellationToken);

        return ServiceResult<LoginOutcome>.Success(
            LoginOutcome.Started(cookie, session, sessionPolicy.AbsoluteLifetime));
    }

    public async Task<ServiceResult<int, PasswordRefusal>> ChangePasswordAsync(
        PasswordChange change,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(change);

        PasswordChangeOutcome outcome = await passwords.ChangeAsync(
            change.Subject,
            change.Keep,
            change.Current,
            change.Replacement,
            hashPolicy,
            cancellationToken);

        switch (outcome.Refusal)
        {
            case PasswordChangeRefusal.WrongPassword:
                return ServiceResult<int, PasswordRefusal>.Failure(
                    "The current password is wrong.",
                    PasswordRefusal.WrongPassword);
            case PasswordChangeRefusal.OutOfLength:
                return ServiceResult<int, PasswordRefusal>.Failure(
                    $"A password is between {LocalAccount.ShortestPassword} and {LocalAccount.LongestPassword} characters long.",
                    PasswordRefusal.OutOfLength);
        }

        await accounts.SaveAsync(outcome.Changed!, cancellationToken);

        if (outcome.Ended.Count > 0)
        {
            await sessions.SaveAllAsync(outcome.Ended, cancellationToken);
        }

        grants.RevokeEverythingOf(change.Subject);
        playlists.RevokeEverythingOf(change.Subject);

        return ServiceResult<int, PasswordRefusal>.Success(outcome.Ended.Count);
    }

    private async Task RemakeTheStoredHashIfItIsWeakerThanThePolicyAsync(
        LocalAccount account,
        string password,
        CancellationToken cancellationToken)
    {
        if (!hashPolicy.NeedsRehash(account.PasswordHash))
        {
            return;
        }

        try
        {
            account.ReplaceTheHashOfTheSamePassword(hasher.Hash(password, hashPolicy));

            await accounts.SaveAsync(account, cancellationToken);
        }
        catch (Exception)
        {
        }
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
