using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ArchivedSupersessionMigrationTests
{
    private const string ScratchDatabase = "carina_archived_supersession_test";

    private const string BeforeTheArchiveWasJudged = "20261014085504_TheArchiveKeepsWhenAProgrammeWasLastHeard";

    private const string HeardToday = "timestamptz '2026-08-18 03:00:00+00'";

    private const string HeardTheDayBefore = "timestamptz '2026-08-17 11:00:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task WhatTheArchiveAlreadyHeldIsJudgedAgainstTheGuideAndTheArchive()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeTheArchiveWasJudged, Cancel);

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await AnnounceAsync(writing, 1, 101, "04:00", "08:10", HeardToday);
            await KeepAsync(writing, 1, 1, "04:00", "05:00", "NULL");
            await KeepAsync(writing, 1, 2, "05:00", "06:00", "NULL");
            await KeepAsync(writing, 1, 3, "08:10", "09:00", "NULL");

            await KeepAsync(writing, 2, 1, "04:00", "05:00", "NULL");
            await KeepAsync(writing, 2, 2, "04:30", "05:30", "NULL");

            await KeepAsync(writing, 3, 1, "04:00", "05:00", HeardTheDayBefore);
            await KeepAsync(writing, 3, 2, "04:30", "05:30", "NULL");
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal(
            ["1-1", "1-2", "3-2"],
            await SupersededAsync(reading));
    }

    private static async Task AnnounceAsync(
        NpgsqlConnection connection,
        int service,
        int eventId,
        string starts,
        string ends,
        string heard)
    {
        await using var writing = new NpgsqlCommand(
            $"""
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id, start_at, end_at, name, summary,
                is_shadow, genres, items, related, has_subtitles, source, updated_at, last_heard_at,
                audio, sounds, video, aspect, running)
            VALUES (
                4, {service}, {eventId}, 32736,
                timestamptz '2026-08-18 {starts}:00+00', timestamptz '2026-08-18 {ends}:00+00',
                'a programme', '', false, '[]', '[]', '[]', false, 'ScheduleBasic', {heard}, {heard},
                'Stereo', 1, 'Interlaced1080', 'SixteenByNine', 'Undetermined')
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task KeepAsync(
        NpgsqlConnection connection,
        int service,
        int eventId,
        string starts,
        string ends,
        string heard)
    {
        await using var writing = new NpgsqlCommand(
            $"""
            INSERT INTO archived_programme (
                network_id, service_id, event_id, start_at, end_at, name, summary, has_subtitles, genres, items,
                archived_at, last_heard_at)
            VALUES (
                4, {service}, {eventId},
                timestamptz '2026-08-18 {starts}:00+00', timestamptz '2026-08-18 {ends}:00+00',
                'a programme', '', false, '[]', '[]', timestamptz '2026-08-19 12:00:00+00', {heard})
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string[]> SupersededAsync(NpgsqlConnection connection)
    {
        await using var reading = new NpgsqlCommand(
            "SELECT service_id || '-' || event_id FROM archived_programme WHERE superseded ORDER BY 1",
            connection);
        await using NpgsqlDataReader rows = await reading.ExecuteReaderAsync(Cancel);
        List<string> found = [];

        while (await rows.ReadAsync(Cancel))
        {
            found.Add(rows.GetString(0));
        }

        return [.. found];
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
