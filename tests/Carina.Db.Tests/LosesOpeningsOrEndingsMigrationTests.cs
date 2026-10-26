using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class LosesOpeningsOrEndingsMigrationTests
{
    private const string ScratchDatabase = "carina_loses_openings_or_endings_test";

    private const string BeforeTheVerdict = "20261026084821_RoundsOfLearningKeepTheFiguresOfTheirCandidate";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-008: a round may say it kept out a candidate that loses openings or endings once the migration is in, the rounds kept before stay as they were, and going back leaves such a round without a verdict")]
    public async Task ARoundMaySayItKeptOutACandidateThatLosesOpeningsOrEndings()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheVerdict, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await RunAsync(before, Round("2026-10-25", "'NoBetterAtBoundaries'"));
            Assert.Equal("23514", await RefusedAsync(before, Round("2026-10-26", "'LosesOpeningsOrEndings'")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync())
        {
            await RunAsync(forward, Round("2026-10-26", "'LosesOpeningsOrEndings'"));
            Assert.Equal("NoBetterAtBoundaries LosesOpeningsOrEndings", await VerdictsAsync(forward));
            Assert.Equal("23514", await RefusedAsync(forward, Round("2026-10-27", "'LosesTheAdverts'")));
        }

        await migrator.MigrateAsync(BeforeTheVerdict, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("NoBetterAtBoundaries -", await VerdictsAsync(back));
            Assert.Equal("23514", await RefusedAsync(back, Round("2026-10-27", "'LosesOpeningsOrEndings'")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Round(string day, string verdict)
        => $"INSERT INTO segment_learning_round (day, taken_at, candidate, outcome, verdict) VALUES (date '{day}', timestamptz '{day} 18:00:00+00', 4, 'Kept', {verdict})";

    private static async Task<string> VerdictsAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand reading = new("SELECT string_agg(coalesce(verdict, '-'), ' ' ORDER BY day) FROM segment_learning_round", connection);

        return (string)(await reading.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<string?> RefusedAsync(NpgsqlConnection connection, string sql)
    {
        try
        {
            await RunAsync(connection, sql);

            return null;
        }
        catch (PostgresException refused)
        {
            return refused.SqlState;
        }
    }

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand running = new(sql, connection);

        await running.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);

        return connection;
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
