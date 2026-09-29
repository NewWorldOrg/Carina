using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ClientSecretSealMigrationTests
{
    private const string ScratchDatabase = "carina_client_secret_seal_test";

    private const string BeforeTheSeal = "20260928224400_AThumbnailTheRowCallsDrawnIsLookedFor";

    private const string TheSeal = "20260929160610_TheClientSecretIsSealed";

    private const string CheckViolation = "23514";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ASecretHeldInTheClearBeforeTheMigrationIsStillThereForTheAppToSeal()
    {
        await using CarinaDbContext context = await HeldInTheClearAsync();

        await context.GetService<IMigrator>().MigrateAsync(TheSeal, Cancel);

        Assert.Equal(["the-client-secret|"], await RowsAsync());
    }

    [Fact]
    public async Task ASecretIsNeverHeldInTheClearAndSealedAtOnce()
    {
        await using CarinaDbContext context = await HeldInTheClearAsync();
        await context.GetService<IMigrator>().MigrateAsync(TheSeal, Cancel);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync("UPDATE oidc_config SET client_secret_sealed = 'sealed'"));

        Assert.Equal(CheckViolation, refused.SqlState);
    }

    [Fact]
    public async Task AProviderWithNeitherFormOfItsSecretCannotBeHeld()
    {
        await using CarinaDbContext context = await HeldInTheClearAsync();
        await context.GetService<IMigrator>().MigrateAsync(TheSeal, Cancel);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync("UPDATE oidc_config SET client_secret = NULL"));

        Assert.Equal(CheckViolation, refused.SqlState);
    }

    [Fact]
    public async Task RollingBackForgetsAProviderWhoseSecretIsOnlySealedRatherThanHandingTheSealToCodeThatReadsItInTheClear()
    {
        await using CarinaDbContext context = await HeldInTheClearAsync();
        IMigrator migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(TheSeal, Cancel);
        await ExecuteAsync(
            "UPDATE oidc_config SET client_secret = NULL, client_secret_sealed = 'sealed', allowed_groups = ARRAY['operators']");
        await migrator.MigrateAsync(BeforeTheSeal, Cancel);

        Assert.Equal(["||0"], await ForgottenAsync());
    }

    [Fact]
    public async Task RollingBackKeepsAProviderWhoseSecretIsStillHeldInTheClear()
    {
        await using CarinaDbContext context = await HeldInTheClearAsync();
        IMigrator migrator = context.GetService<IMigrator>();

        await migrator.MigrateAsync(TheSeal, Cancel);
        await migrator.MigrateAsync(BeforeTheSeal, Cancel);

        Assert.Equal(["https://login.example.test/c|the-client-secret|0"], await ForgottenAsync());
    }

    private static async Task<CarinaDbContext> HeldInTheClearAsync()
    {
        CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(BeforeTheSeal, Cancel);

        await ExecuteAsync(
            "INSERT INTO oidc_config (id, discovery_url, client_id, client_secret, updated_at) "
            + "VALUES (1, 'https://login.example.test/c', 'carina', 'the-client-secret', now())");

        return context;
    }

    private static async Task<List<string>> RowsAsync()
    {
        await using CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch());

        return await reading.Database
            .SqlQueryRaw<string>(
                "SELECT coalesce(client_secret, '') || '|' || coalesce(client_secret_sealed, '') AS \"Value\" FROM oidc_config")
            .ToListAsync(Cancel);
    }

    private static async Task<List<string>> ForgottenAsync()
    {
        await using CarinaDbContext reading = CarinaDbContextFactory.Create(Scratch());

        return await reading.Database
            .SqlQueryRaw<string>(
                "SELECT coalesce(discovery_url, '') || '|' || coalesce(client_secret, '') || '|' "
                + "|| coalesce(array_length(allowed_groups, 1), 0) AS \"Value\" FROM oidc_config")
            .ToListAsync(Cancel);
    }

    private static async Task ExecuteAsync(string sql)
    {
        await using NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static string Scratch()
    {
        string? configured = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable);
        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new InvalidOperationException(
                $"DbIntegration tests need {CarinaDbContextFactory.ConnectionStringVariable} pointing at the compose db service.");
        }

        return new NpgsqlConnectionStringBuilder(configured) { Database = ScratchDatabase }.ConnectionString;
    }
}
