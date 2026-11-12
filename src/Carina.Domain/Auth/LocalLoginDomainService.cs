namespace Carina.Domain.Auth;

/// <summary>
/// The answer to one sign-in to the local account: the account it opened, the moment the caller may try again, or
/// neither when the name or the password is wrong.
/// </summary>
public sealed record LocalLogin(LocalAccount? Admitted, DateTime? HeldOffUntil)
{
    public static LocalLogin HeldOff(DateTime until) => new(null, until);

    public static LocalLogin Refused { get; } = new(null, null);

    public static LocalLogin Opened(LocalAccount account) => new(account, null);
}

/// <summary>
/// Decides a sign-in to the local account: counts the caller's try before the password is checked, compares the
/// password with the stored hash, or with a hash of the same cost when there is no account so that the time taken
/// says nothing, and forgets the caller's tries once one passes.
/// </summary>
public sealed class LocalLoginDomainService(
    ILocalAccountRepository accounts,
    IPasswordHasher hasher,
    ILoginThrottle throttle)
{
    public async Task<LocalLogin> TryAsync(
        string caller,
        string username,
        string password,
        PasswordHashPolicy policy,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (throttle.TakeTry(caller) is { } until)
        {
            return LocalLogin.HeldOff(until);
        }

        LocalAccount? account = await accounts.FindAsync(cancellationToken);

        if (!Admits(account, username, password, policy))
        {
            return LocalLogin.Refused;
        }

        throttle.Passed(caller);

        return LocalLogin.Opened(account!);
    }

    private bool Admits(LocalAccount? account, string username, string password, PasswordHashPolicy policy)
    {
        if (!LocalAccount.IsAPassword(password))
        {
            return false;
        }

        bool digestsAgree = hasher.Matches(password, account?.PasswordHash ?? Decoy(policy));

        return account is not null && digestsAgree && account.IsSignedInToAs(username);
    }

    private static PasswordHash Decoy(PasswordHashPolicy policy)
        => PasswordHash.Encode(policy, new byte[policy.SaltLength], new byte[policy.DigestLength]);
}
