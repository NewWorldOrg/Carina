namespace Carina.Domain.Auth;

public sealed record LocalLogin(LocalAccount? Admitted, DateTime? HeldOffUntil)
{
    public static LocalLogin HeldOff(DateTime until) => new(null, until);

    public static LocalLogin Refused { get; } = new(null, null);

    public static LocalLogin Opened(LocalAccount account) => new(account, null);
}

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
