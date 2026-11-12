namespace Carina.Domain.Auth;

/// <summary>
/// The local account made when none existed, and its password in the clear, which is told once and kept nowhere.
/// </summary>
public sealed record FirstLocalAccount(LocalAccount Account, string Password)
{
    public override string ToString() => "the first local account";
}

/// <summary>
/// Makes the local account when there is none, so that the app never waits without one, and leaves an account that
/// exists alone, looking it up.
/// </summary>
public sealed class LocalAccountProvisioningDomainService(
    ILocalAccountRepository accounts,
    IPasswordHasher hasher,
    TimeProvider clock)
{
    /// <summary>
    /// The account to save and the password to tell, when no account exists; nothing when one does. Nothing is
    /// written here.
    /// </summary>
    public async Task<FirstLocalAccount?> FirstAccountAsync(PasswordHashPolicy policy, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(policy);

        if (await accounts.FindAsync(cancellationToken) is not null)
        {
            return null;
        }

        string password = FirstCredentials.MakePassword();
        LocalAccount account = LocalAccount.Bootstrap(
            FirstCredentials.Username,
            hasher.Hash(password, policy),
            clock.GetUtcNow().UtcDateTime);

        return new FirstLocalAccount(account, password);
    }
}
