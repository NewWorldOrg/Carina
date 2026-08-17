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

        if (context.Database.CurrentTransaction is { } ambient)
        {
            return await WithinAsync(ambient, write, cancellationToken);
        }

        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

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

    private static async Task<T> WithinAsync<T>(
        IDbContextTransaction ambient,
        Func<CancellationToken, Task<T>> write,
        CancellationToken cancellationToken)
    {
        if (!ambient.SupportsSavepoints)
        {
            return await write(cancellationToken);
        }

        var name = "write_" + Guid.NewGuid().ToString("n");

        await ambient.CreateSavepointAsync(name, cancellationToken);

        T written;

        try
        {
            written = await write(cancellationToken);
        }
        catch
        {
            await UndoToAsync(ambient, name);

            throw;
        }

        await ambient.ReleaseSavepointAsync(name, CancellationToken.None);

        return written;
    }

    private static async Task UndoToAsync(IDbContextTransaction ambient, string name)
    {
        try
        {
            await ambient.RollbackToSavepointAsync(name, CancellationToken.None);
        }
        catch (Exception undoing) when (undoing is not OperationCanceledException)
        {
        }
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
