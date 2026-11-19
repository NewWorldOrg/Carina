namespace Carina.Domain.Auth;

public sealed record PasswordChangeOutcome(
    PasswordChangeRefusal? Refusal,
    LocalAccount? Changed,
    IReadOnlyList<AuthSession> Ended)
{
    public static PasswordChangeOutcome Refused(PasswordChangeRefusal refusal) => new(refusal, null, []);

    public static PasswordChangeOutcome Made(LocalAccount changed, IReadOnlyList<AuthSession> ended) => new(null, changed, ended);
}

public sealed class PasswordChangeDomainService(
    ILocalAccountRepository accounts,
    IAuthSessionRepository sessions,
    IPasswordHasher hasher,
    TimeProvider clock)
{
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
