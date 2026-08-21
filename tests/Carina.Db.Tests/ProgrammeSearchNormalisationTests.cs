using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeSearchNormalisationTests
{
    private const string ScratchDatabase = "carina_programme_normalisation_test";

    private const string BeforeNormalisation = "20260821014556_ProgrammeGenreKinds";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    private const string At = "timestamptz '2026-08-21 12:00:00+00'";

    [Fact]
    public async Task ProgrammesWrittenBeforeTheNormalisationAreBroughtToItsShape()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync();

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeNormalisation, Cancel);

        await using (NpgsqlConnection connection = await OpenAsync())
        {
            await BroadcastAsync(connection, 1, "ﾆｭｰｽ①", "ｷﾞｮｳｻﾞ");

            Assert.Equal("ﾆｭｰｽ① ｷﾞｮｳｻﾞ", await SearchableAsync(connection, 1));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal("ニュース1 ギョウザ", await SearchableAsync(reading, 1));
    }

    [Fact]
    public async Task TheIndexTheSearchRunsOnSurvivesTheNormalisation()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync();
        await context.Database.MigrateAsync(Cancel);

        await using NpgsqlConnection connection = await OpenAsync();
        await BroadcastAsync(connection, 2, "ﾆｭｰｽ", string.Empty);

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexdef FROM pg_indexes WHERE tablename = 'programme' AND indexname = 'ix_programme_searchable'";

        Assert.Contains(
            "gin (searchable gin_trgm_ops)",
            (string?)await command.ExecuteScalarAsync(Cancel) ?? string.Empty,
            StringComparison.Ordinal);
        Assert.Equal("ニュース ", await SearchableAsync(connection, 2));
    }

    private static async Task BroadcastAsync(NpgsqlConnection connection, int carried, string name, string summary)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id,
                start_at, end_at, name, summary, is_shadow,
                genres, items, related, has_subtitles, source, updated_at)
            VALUES (
                1, 1049, @event, 1,
                {At}, {At} + interval '30 minutes', @name, @summary, false,
                '[]', '[]', '[]', false, 'ScheduleBasic', {At})
            """;
        command.Parameters.AddWithValue("event", carried);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("summary", summary);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> SearchableAsync(NpgsqlConnection connection, int carried)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = "SELECT searchable FROM programme WHERE event_id = @event";
        command.Parameters.AddWithValue("event", carried);

        return (string)(await command.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        var connection = new NpgsqlConnection(Scratch());
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
