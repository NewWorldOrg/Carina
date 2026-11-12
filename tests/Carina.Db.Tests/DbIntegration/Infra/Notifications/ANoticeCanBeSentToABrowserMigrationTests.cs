using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests.DbIntegration.Infra.Notifications;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ANoticeCanBeSentToABrowserMigrationTests
{
    private const string ScratchDatabase = "carina_push_destination_migration_test";

    private const string BeforeBrowsers = "20261110182416_AProgrammeCanBeWatchedWithoutBeingRecorded";

    private const string At = "timestamptz '2026-11-09 12:00:00+00'";

    private const string Webhook = "0b0e1c52-6f0f-4f0e-9a52-2f8f5a7f3c11";

    private const string Browser = "5f6f2b0a-83c4-4d0b-b6a1-0d4b8a2c7e22";

    private const string Hash = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-ND-015: a webhook registered before browsers could be told comes through with no hash, and a browser is kept beside it")]
    public async Task AWebhookRegisteredBeforeComesThroughWithNoHash()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeBrowsers, Cancel);

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await RunAsync(before, AWebhookOfBefore());
            Assert.Equal("23514", await RefusedAsync(before, ADestinationOfBefore(Browser, "Push")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection after = await OpenAsync())
        {
            await RunAsync(after, ABrowser());

            Assert.Equal(
                $"Push:{Hash} Slack:",
                await ScalarAsync(after, "SELECT string_agg(target || ':' || coalesce(endpoint_hash, ''), ' ' ORDER BY target) FROM notification_destination"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    [Fact(DisplayName = "BR-ND-015: going back to before browsers could be told takes away the browsers with their deliveries, and leaves the webhooks and theirs")]
    public async Task GoingBackTakesAwayTheBrowsers()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync())
        {
            await RunAsync(forward, AWebhookOfBefore());
            await RunAsync(forward, ABrowser());
            await RunAsync(forward, "INSERT INTO notification_event (id, kind, source_id, occurred_at, tone, title, body, link, taken_at) VALUES (gen_random_uuid(), 'RecordingStarted', gen_random_uuid(), " + At + ", 'Informative', 'A title', 'A body', NULL, " + At + ")");
            await RunAsync(forward, ADeliveryTo(Webhook));
            await RunAsync(forward, ADeliveryTo(Browser));
        }

        await migrator.MigrateAsync(BeforeBrowsers, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("Slack", await ScalarAsync(back, "SELECT string_agg(target, ' ' ORDER BY target) FROM notification_destination"));
            Assert.Equal(Webhook, await ScalarAsync(back, "SELECT string_agg(destination_id::text, ' ') FROM notification_delivery"));
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*)::text FROM information_schema.columns WHERE table_name = 'notification_destination' AND column_name = 'endpoint_hash'"));
            Assert.Equal("23514", await RefusedAsync(back, ADestinationOfBefore(Browser, "Push")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string AWebhookOfBefore() => ADestinationOfBefore(Webhook, "Slack");

    private static string ADestinationOfBefore(string id, string target)
        => $"INSERT INTO notification_destination (id, enabled, created_at, changed_at, name, target, sealed_url, url_tail, last_result_at, last_failure) VALUES ('{id}', TRUE, {At}, {At}, 'A destination', '{target}', '\\x01'::bytea, 'wxyz', NULL, NULL)";

    private static string ABrowser()
        => $"INSERT INTO notification_destination (id, enabled, created_at, changed_at, name, target, sealed_url, url_tail, last_result_at, last_failure, endpoint_hash) VALUES ('{Browser}', TRUE, {At}, {At}, 'A browser', 'Push', '\\x01'::bytea, 'cdef', NULL, NULL, '{Hash}')";

    private static string ADeliveryTo(string destination)
        => $"INSERT INTO notification_delivery (id, event_id, destination_id, queued_at, state, attempts, next_attempt_at, sent_at, last_failure) SELECT gen_random_uuid(), id, '{destination}', {At}, 'Queued', 0, {At}, NULL, NULL FROM notification_event";

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
