using System.Data;

using Carina.Domain.Migration;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Infrastructure.Migration;

public sealed class MigrationLease(CarinaDbContext context) : IMigrationLease
{
    public const long Key = 5_243_197_610_002;

    public async Task<IAsyncDisposable?> TakeAsync(CancellationToken cancellationToken)
    {
        bool wasClosed = context.Database.GetDbConnection().State is ConnectionState.Closed;

        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync(cancellationToken);
        }

        bool taken = await context
            .Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_lock({Key}) AS \"Value\"")
            .SingleAsync(cancellationToken);

        if (taken)
        {
            return new Hold(context, wasClosed);
        }

        if (wasClosed)
        {
            await context.Database.CloseConnectionAsync();
        }

        return null;
    }

    private sealed class Hold(CarinaDbContext context, bool closeWhenDone) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await context.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({Key})");

            if (closeWhenDone)
            {
                await context.Database.CloseConnectionAsync();
            }
        }
    }
}
