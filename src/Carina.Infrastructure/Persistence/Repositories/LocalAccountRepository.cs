using Carina.Domain.Auth;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class LocalAccountRepository(CarinaDbContext context) : ILocalAccountRepository
{
    public async Task<LocalAccount?> FindAsync(CancellationToken cancellationToken)
        => await context.Set<LocalAccount>()
            .FirstOrDefaultAsync(account => account.Id == LocalAccount.TheOnlyRow, cancellationToken);

    public async Task SaveAsync(LocalAccount account, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(account);

        if (context.Entry(account).State is EntityState.Detached)
        {
            await context.AddAsync(account, cancellationToken);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
