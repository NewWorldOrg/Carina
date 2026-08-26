using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ArchivedProgrammeSearchColumnsTests
{
    private const string ScratchDatabase = "carina_archived_search_test";

    private const string BeforeTheColumns = "20260821083911_ProgrammeSearchNormalisation";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task WhatTheArchiveAlreadyHeldIsGivenTheColumnsTheSearchRunsOn()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheColumns, Cancel);

        await using (NpgsqlConnection connection = await OpenAsync())
        {
            await KeptAsync(connection, 1, "ﾆｭｰｽ①", "ｷﾞｮｳｻﾞ");
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal("ニュース1 ギョウザ", await ScalarAsync(reading, "searchable::text"));
        Assert.Equal("{8}", await ScalarAsync(reading, "genre_kinds::text"));
    }

    [Fact]
    public async Task TheArchiveCarriesTheSameKindOfSearchIndexTheGuideDoes()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'archived_programme' AND indexname = 'ix_archived_programme_searchable'";

        Assert.Contains(
            "gin (searchable gin_trgm_ops)",
            (string?)await command.ExecuteScalarAsync(Cancel) ?? string.Empty,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task BothSearchColumnsAreSampledDeeplyEnoughForOnePlanToKeepWinning()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT attrelid::regclass::text || ' ' || coalesce(attstattarget::text, 'the default') "
            + "FROM pg_attribute "
            + "WHERE attrelid IN ('programme'::regclass, 'archived_programme'::regclass) "
            + "AND attname = 'searchable' ORDER BY 1";

        await using NpgsqlDataReader rows = await command.ExecuteReaderAsync(Cancel);
        List<string> read = [];

        while (await rows.ReadAsync(Cancel))
        {
            read.Add(rows.GetString(0));
        }

        Assert.Equal(["archived_programme 1000", "programme 1000"], read);
    }

    [Fact]
    public async Task TheEndIndexCarriesWhatTheSearchJoinsOnSoTheCountNeverTouchesTheHeap()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        await context.GetService<IMigrator>().MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection connection = await OpenAsync();
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            "SELECT indexdef FROM pg_indexes "
            + "WHERE tablename = 'archived_programme' AND indexname = 'ix_archived_programme_end_at'";

        Assert.Equal(
            "CREATE INDEX ix_archived_programme_end_at ON public.archived_programme "
            + "USING btree (end_at) INCLUDE (network_id, service_id, event_id, start_at)",
            (string?)await command.ExecuteScalarAsync(Cancel));
    }

    private static async Task KeptAsync(NpgsqlConnection connection, int carried, string name, string summary)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO archived_programme (
                network_id, service_id, event_id, start_at, end_at,
                name, summary, has_subtitles, genres, items, archived_at)
            VALUES (
                1, 1049, @event,
                timestamptz '2026-08-01 12:00:00+00', timestamptz '2026-08-01 12:30:00+00',
                @name, @summary, false, '[{"kind": 8, "sort": 0}]', '[]',
                timestamptz '2026-08-02 12:00:00+00')
            """;
        command.Parameters.AddWithValue("event", carried);
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("summary", summary);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string column)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM archived_programme";

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
