using Carina.Domain.Base;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

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

        T written;

        try
        {
            written = await write(cancellationToken);
        }
        catch
        {
            // Undone here rather than on disposal: a rollback that fails on its way out would
            // otherwise replace the failure that caused it, and that one is the one worth having.
            await RollBackAsync(transaction);

            throw;
        }

        // The work is done and the caller is owed a definite answer; a cancellation landing on
        // the commit itself would leave which of the two outcomes happened unknowable.
        await transaction.CommitAsync(CancellationToken.None);

        return written;
    }

    private static async Task RollBackAsync(IDbContextTransaction transaction)
    {
        try
        {
            await transaction.RollbackAsync(CancellationToken.None);
        }
        catch (Exception undoing) when (undoing is not OperationCanceledException)
        {
            // A connection that died mid-write cannot be told to undo it, and the server undoes
            // it anyway when the connection goes. Nothing was committed either way.
        }
    }
}
