using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeAutoRunRepository(CarinaDbContext context) : IEncodeAutoRunRepository
{
    public async Task<EncodeAutoRun?> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<EncodeAutoRun>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == EncodeAutoRun.TheOnlyRow, cancellationToken);

    public async Task SaveAsync(EncodeAutoRun autoRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(autoRun);

        bool held = await context.Set<EncodeAutoRun>()
            .AsNoTracking()
            .AnyAsync(row => row.Id == autoRun.Id, cancellationToken);

        if (held)
        {
            context.Update(autoRun);
        }
        else
        {
            context.Add(autoRun);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
