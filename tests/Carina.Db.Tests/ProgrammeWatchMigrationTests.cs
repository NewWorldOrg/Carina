using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeWatchMigrationTests
{
    private const string ScratchDatabase = "carina_programme_watch_migration_test";

    private const string BeforeProgrammesWereAskedFor = "20261110112205_NoticesAreLedgeredAndSentToWebhooks";

    private const string At = "timestamptz '2026-11-09 12:00:00+00'";

    private const string Destination = "0b0e1c52-6f0f-4f0e-9a52-2f8f5a7f3c11";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-NT-004: going back to before a programme could be asked for takes away the notices of its three kinds with their deliveries, and leaves the others")]
    public async Task GoingBackTakesAwayTheNoticesOfAProgrammeAskedFor()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync())
        {
            await RunAsync(
                forward,
                $"INSERT INTO notification_destination (id, enabled, created_at, changed_at, name, target, sealed_url, url_tail, last_result_at, last_failure) VALUES ('{Destination}', TRUE, {At}, {At}, 'A destination', 'Slack', '\\x01'::bytea, 'wxyz', NULL, NULL)");

            foreach (string kind in (string[])["RecordingStarted", "ProgrammeStarting", "WatchedProgrammeMoved", "WatchedProgrammeGone"])
            {
                await RunAsync(forward, AnEvent(kind));
                await RunAsync(forward, ADeliveryOfThe(kind));
            }
        }

        await migrator.MigrateAsync(BeforeProgrammesWereAskedFor, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("RecordingStarted", await ScalarAsync(back, "SELECT string_agg(kind, ' ' ORDER BY kind) FROM notification_event"));
            Assert.Equal("1", await ScalarAsync(back, "SELECT count(*)::text FROM notification_delivery"));
            Assert.Equal("23514", await RefusedAsync(back, AnEvent("ProgrammeStarting")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string AnEvent(string kind)
        => $"INSERT INTO notification_event (id, kind, source_id, occurred_at, tone, title, body, link, taken_at) VALUES (gen_random_uuid(), '{kind}', gen_random_uuid(), {At}, 'Informative', 'A title', 'A body', NULL, {At})";

    private static string ADeliveryOfThe(string kind)
        => $"INSERT INTO notification_delivery (id, event_id, destination_id, queued_at, state, attempts, next_attempt_at, sent_at, last_failure) SELECT gen_random_uuid(), id, '{Destination}', {At}, 'Queued', 0, {At}, NULL, NULL FROM notification_event WHERE kind = '{kind}'";

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new(sql, connection);

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
