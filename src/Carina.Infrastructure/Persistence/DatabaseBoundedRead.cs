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

        await using IDbContextTransaction transaction =
            await context.Database.BeginTransactionAsync(cancellationToken);

        T found;

        try
        {
            await context.Database.ExecuteSqlRawAsync(Bounding(patience), cancellationToken);

            found = await read(cancellationToken);
        }
        catch (PostgresException raised) when (raised.SqlState == PostgresErrorCodes.QueryCanceled)
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
