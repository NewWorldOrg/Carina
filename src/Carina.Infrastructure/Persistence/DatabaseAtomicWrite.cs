using Carina.Domain.Base;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Carina.Infrastructure.Persistence;

public sealed class DatabaseAtomicWrite(CarinaDbContext context) : IAtomicWrite
{
    public async Task<T> AllOrNothingAsync<T>(
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(write);

        if (context.Database.CurrentTransaction is not null)
        {
            throw new NestedWriteRefusedException();
        }

        await using IDbContextTransaction transaction = await context.Database.BeginTransactionAsync(cancellationToken);

        T written;

        try
        {
            written = await write(cancellationToken);
        }
        catch
        {
            await RollBackAsync(transaction);

            throw;
        }

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
        }
    }
}
