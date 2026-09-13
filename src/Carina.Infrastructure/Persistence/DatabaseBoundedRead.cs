using System.Globalization;

using Carina.Domain.Base;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Npgsql;

namespace Carina.Infrastructure.Persistence;

public sealed class DatabaseBoundedRead(CarinaDbContext context) : IBoundedRead
{
    public async Task<T> NoLongerThanAsync<T>(
        TimeSpan patience,
        Func<CancellationToken, Task<T>> read,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(patience, TimeSpan.Zero);

        if (context.Database.CurrentTransaction is not null)
        {
            throw new NestedReadRefusedException();
        }

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        T found;

        try
        {
            await context.Database.ExecuteSqlRawAsync(Bounding(patience), cancellationToken);

            found = await read(cancellationToken);
        }
        catch (Exception raised) when (!cancellationToken.IsCancellationRequested && RanOutOfTime(raised))
        {
            await LetGoAsync(transaction);

            throw new ReadTookTooLongException(patience, raised);
        }
        catch
        {
            await LetGoAsync(transaction);

            throw;
        }

        await transaction.CommitAsync(CancellationToken.None);

        return found;
    }

    private static bool RanOutOfTime(Exception raised)
    {
        for (Exception? walking = raised; walking is not null; walking = walking.InnerException)
        {
            if (walking is PostgresException stopped && stopped.SqlState == PostgresErrorCodes.QueryCanceled)
            {
                return true;
            }
        }

        return false;
    }

    private static string Bounding(TimeSpan patience)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"SET LOCAL statement_timeout = {(long)Math.Ceiling(patience.TotalMilliseconds)}");

    private static async Task LetGoAsync(IDbContextTransaction transaction)
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
