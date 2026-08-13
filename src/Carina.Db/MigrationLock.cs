using System.Data;

using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

namespace Carina.Db;

public sealed class MigrationLock : IAsyncDisposable
{
    public const long Key = 5_243_197_610_001;

    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(10);

    private readonly CarinaDbContext context;
    private readonly bool closeWhenDone;

    private MigrationLock(CarinaDbContext context, bool closeWhenDone)
    {
        this.context = context;
        this.closeWhenDone = closeWhenDone;
    }

    public static async Task<MigrationLock> TakeAsync(CarinaDbContext context, TextWriter error)
    {
        var wasClosed = context.Database.GetDbConnection().State is ConnectionState.Closed;

        if (wasClosed)
        {
            await context.Database.OpenConnectionAsync();
        }

        var taken = await context
            .Database.SqlQueryRaw<bool>($"SELECT pg_try_advisory_lock({Key}) AS \"Value\"")
            .SingleAsync();

        if (!taken)
        {
            await error.WriteLineAsync(
                $"Another --migrate holds the migration lock; waiting up to {WaitLimit.TotalMinutes:0} minutes for it to finish."
            );

            var previousTimeout = context.Database.GetCommandTimeout();
            context.Database.SetCommandTimeout(WaitLimit);

            try
            {
                await context.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_lock({Key})");
            }
            finally
            {
                context.Database.SetCommandTimeout(previousTimeout);
            }
        }

        return new MigrationLock(context, wasClosed);
    }

    public async ValueTask DisposeAsync()
    {
        await context.Database.ExecuteSqlRawAsync($"SELECT pg_advisory_unlock({Key})");

        if (closeWhenDone)
        {
            await context.Database.CloseConnectionAsync();
        }
    }
}
