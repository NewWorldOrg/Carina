using Carina.Domain.Encodings;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence.Repositories;

public sealed class EncodeAutoRunRepository(CarinaDbContext context) : IEncodeAutoRunRepository
{
    public async Task<EncodeAutoRun?> ReadAsync(CancellationToken cancellationToken)
        => await context.Set<EncodeAutoRun>()
            .AsNoTracking()
            .FirstOrDefaultAsync(row => row.Id == EncodeAutoRun.TheOnlyRow, cancellationToken);

    /// <summary>
    /// One statement, because looking first and then writing is two: on a table that holds no row
    /// yet, two hands that both look before either writes both decide to insert, and the slower one
    /// is refused by the primary key.
    /// </summary>
    public async Task SaveAsync(EncodeAutoRun autoRun, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(autoRun);

        await context.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO encode_auto_run (id, automatically, most_cores, updated_at)
            VALUES ({autoRun.Id}, {autoRun.Automatically}, {autoRun.MostCores}, {autoRun.UpdatedAt})
            ON CONFLICT (id) DO UPDATE SET
                automatically = excluded.automatically,
                most_cores = excluded.most_cores,
                updated_at = excluded.updated_at
            """,
            cancellationToken);
    }
}
