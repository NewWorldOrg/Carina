using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class MigrationPipelineTests
{
    private const string ScratchDatabase = "carina_migration_test";

    private static string ScratchConnectionString()
    {
        var configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }

    [Fact]
    public async Task AppliesEveryMigrationToAnEmptyDatabase()
    {
        await using var context = CarinaDbContextFactory.Create(ScratchConnectionString());
        await context.Database.EnsureDeletedAsync();

        await context.Database.MigrateAsync();

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToArray();
        Assert.EndsWith("_Initial", applied[0], StringComparison.Ordinal);
        Assert.Equal(context.Database.GetMigrations(), applied);
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());

        // A migration that writes no DDL is only correct while the model still agrees with the
        // snapshot it was generated against. Without this the emptiness is taken on trust.
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task ReRunningTheMigrationsIsIdempotent()
    {
        await using var context = CarinaDbContextFactory.Create(ScratchConnectionString());
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync();
        var firstRun = (await context.Database.GetAppliedMigrationsAsync()).ToArray();

        await context.Database.MigrateAsync();

        Assert.Equal(firstRun, await context.Database.GetAppliedMigrationsAsync());
    }

    [Fact]
    public async Task EntryPointMigratesRepeatablyAndFailsLoudlyOnBadCredentials()
    {
        var connectionString = ScratchConnectionString();

        using (new EnvironmentVariableScope(CarinaDbContextFactory.ConnectionStringVariable, connectionString))
        {
            Assert.Equal(DbEntryPoint.SuccessExitCode, await DbEntryPoint.RunAsync(["--migrate"], new StringWriter()));
            Assert.Equal(DbEntryPoint.SuccessExitCode, await DbEntryPoint.RunAsync(["--migrate"], new StringWriter()));
        }

        var wrongPassword = new NpgsqlConnectionStringBuilder(connectionString) { Password = "wrong" }.ConnectionString;

        using (new EnvironmentVariableScope(CarinaDbContextFactory.ConnectionStringVariable, wrongPassword))
        {
            var error = new StringWriter();

            var exitCode = await DbEntryPoint.RunAsync(["--migrate"], error);

            Assert.Equal(DbEntryPoint.MigrationFailedExitCode, exitCode);
            Assert.Contains("Carina.Db --migrate failed", error.ToString(), StringComparison.Ordinal);
        }
    }
}
