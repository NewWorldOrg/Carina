using Carina.Domain.Channels;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Persistence;

/// <summary>
/// One database transaction around the whole write. A repository that opens its own
/// transaction joins this one instead, so the piece it meant to make atomic on its own
/// does not commit ahead of the write it is part of.
/// </summary>
public sealed class DatabaseAtomicWrite(CarinaDbContext context) : IAtomicWrite
{
    public async Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        if (context.Database.CurrentTransaction is not null)
        {
            return await write(cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        var written = await write(cancellationToken);

        // The work is done and the caller is owed a definite answer; a cancellation landing on
        // the commit itself would leave which of the two outcomes happened unknowable.
        await transaction.CommitAsync(CancellationToken.None);

        return written;
    }
}
