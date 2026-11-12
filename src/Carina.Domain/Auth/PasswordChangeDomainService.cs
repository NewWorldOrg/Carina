namespace Carina.Domain.Auth;

/// <summary>
/// A password changed, with the sessions it ended to be written, or why it was not changed.
/// </summary>
public sealed record PasswordChangeOutcome(
    PasswordChangeRefusal? Refusal,
    LocalAccount? Changed,
    IReadOnlyList<AuthSession> Ended)
{
    public static PasswordChangeOutcome Refused(PasswordChangeRefusal refusal) => new(refusal, null, []);

    public static PasswordChangeOutcome Made(LocalAccount changed, IReadOnlyList<AuthSession> ended) => new(null, changed, ended);
}

/// <summary>
/// Changes the password of the local account and ends every session of the one who changed it except the one that
/// asked, looking the account and the sessions up.
/// </summary>
public sealed class PasswordChangeDomainService(
    ILocalAccountRepository accounts,
    IAuthSessionRepository sessions,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    /// <summary>
    /// Changes the password from <paramref name="current"/> to <paramref name="replacement"/>, and revokes every
    /// session <paramref name="subject"/> holds but <paramref name="keep"/>. Nothing is written here; the account and
    /// the sessions ended are handed back to be.
    /// </summary>
    public async Task<PasswordChangeOutcome> ChangeAsync(
        Subject subject,
        SessionHandle keep,
        string current,
        string replacement,
        PasswordHashPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentNullException.ThrowIfNull(keep);
        ArgumentNullException.ThrowIfNull(policy);

        LocalAccount? account = await accounts.FindAsync(cancellationToken);

        if (account is null)
        {
            return PasswordChangeOutcome.Refused(PasswordChangeRefusal.WrongPassword);
        }

        if (account.RefusesToChange(current, replacement, hasher) is { } refusal)
        {
            return PasswordChangeOutcome.Refused(refusal);
        }

        DateTime at = clock.GetUtcNow().UtcDateTime;

        account.ChangePassword(hasher.Hash(replacement, policy), at);

        IReadOnlyList<AuthSession> held = await sessions.ListAsync(subject, cancellationToken);
        List<AuthSession> ended = [];

        foreach (AuthSession session in held.Where(session => !session.Handle.Equals(keep)))
        {
            if (session.Revoke(at))
            {
                ended.Add(session);
            }
        }

        return PasswordChangeOutcome.Made(account, ended);
    }
}
