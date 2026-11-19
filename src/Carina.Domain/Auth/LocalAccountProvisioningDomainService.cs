namespace Carina.Domain.Auth;

public sealed record FirstLocalAccount(LocalAccount Account, string Password)
{
    public override string ToString() => "the first local account";
}

public sealed class LocalAccountProvisioningDomainService(
    ILocalAccountRepository accounts,
    IPasswordHasher hasher,
    TimeProvider clock)
{
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
