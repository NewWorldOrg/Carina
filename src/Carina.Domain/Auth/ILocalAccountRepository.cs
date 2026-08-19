namespace Carina.Domain.Auth;

public interface ILocalAccountRepository
{
    Task<LocalAccount?> FindAsync(CancellationToken cancellationToken);

    Task SaveAsync(LocalAccount account, CancellationToken cancellationToken);
}
