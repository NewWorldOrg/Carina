using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationLockTests
{
    private const string ScratchDatabase = "carina_migration_lock_test";

    private static string ScratchConnectionString()
    {
        string? configured = Environment.GetEnvironmentVariable(
            CarinaDbContextFactory.ConnectionStringVariable
        );

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service."
            );
        }

        return new NpgsqlConnectionStringBuilder(configured)
        {
            Database = ScratchDatabase,
        }.ConnectionString;
    }

    private static async Task<NpgsqlConnection> HoldTheLock(string connectionString)
    {
        string unpooled = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Pooling = false,
        }.ConnectionString;

        var holder = new NpgsqlConnection(unpooled);
        await holder.OpenAsync();

        await using NpgsqlCommand command = holder.CreateCommand();
        command.CommandText = $"SELECT pg_advisory_lock({MigrationLock.Key})";
        await command.ExecuteScalarAsync();

        return holder;
    }

    [Fact]
    public async Task ASecondMigrationWaitsForTheOneThatHoldsTheLock()
    {
        string connectionString = ScratchConnectionString();

        await using (CarinaDbContext setup = CarinaDbContextFactory.Create(connectionString))
        {
            await setup.Database.EnsureDeletedAsync();
            await setup.Database.MigrateAsync();
        }

        NpgsqlConnection holder = await HoldTheLock(connectionString);

        using var scope = new EnvironmentVariableScope(
            CarinaDbContextFactory.ConnectionStringVariable,
            connectionString
        );

        var error = new StringWriter();
        Task<int> blocked = Task.Run(() => DbEntryPoint.RunAsync(["--migrate"], error));

        Task finishedEarly = await Task.WhenAny(blocked, Task.Delay(TimeSpan.FromSeconds(3)));

        Assert.NotSame(blocked, finishedEarly);

        await holder.DisposeAsync();

        Assert.Equal(DbEntryPoint.SuccessExitCode, await blocked);
        Assert.Contains("holds the migration lock", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLockIsReleasedSoTheNextMigrationRunsStraightAway()
    {
        string connectionString = ScratchConnectionString();

        using var scope = new EnvironmentVariableScope(
            CarinaDbContextFactory.ConnectionStringVariable,
            connectionString
        );

        Assert.Equal(
            DbEntryPoint.SuccessExitCode,
            await DbEntryPoint.RunAsync(["--migrate"], new StringWriter())
        );

        var error = new StringWriter();

        Assert.Equal(
            DbEntryPoint.SuccessExitCode,
            await DbEntryPoint.RunAsync(["--migrate"], error)
        );

        Assert.DoesNotContain("holds the migration lock", error.ToString(), StringComparison.Ordinal);
    }
}
