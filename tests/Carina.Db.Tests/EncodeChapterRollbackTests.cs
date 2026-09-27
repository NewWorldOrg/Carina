using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeChapterRollbackTests
{
    private const string ScratchDatabase = "carina_encode_chapter_rollback_test";

    private const string BeforeTheBreaksWereJudged = "20260914012253_HowTheQueueRunsWhenNobodyAsked";

    private static readonly Guid Profile = new("0a1b2c3d-4e5f-4061-8283-8485868788a9");

    private static readonly Guid Destination = new("7c1e2f3a-4b5c-4d6e-8f90-a1b2c3d4e5f6");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "chapters already marked do not stop the ledger going back to before anything looked for them")]
    public async Task ChaptersAlreadyMarkedDoNotStopTheLedgerGoingBack()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        var job = Guid.NewGuid();

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await SeedAsync(writing);
            await JobAsync(writing, job);
            await ChapterAsync(writing, job, 0, "interval '0'", "interval '00:00:30'", "Programme");
            await ChapterAsync(writing, job, 1, "interval '00:00:30'", "interval '00:01:30'", "Break");

            Assert.Equal(2L, await CountAsync(writing, "SELECT count(*) FROM encode_chapter"));
            Assert.Equal(
                1L,
                await CountAsync(writing, $"SELECT count(*) FROM encode_job WHERE id = '{job}' AND chapters_verdict = 'Marked'"));
        }

        await migrator.MigrateAsync(BeforeTheBreaksWereJudged, Cancel);

        await using NpgsqlConnection reading = await OpenAsync();

        Assert.Equal(0L, await CountAsync(reading, "SELECT count(*) FROM pg_class WHERE relname = 'encode_chapter'"));
        Assert.Equal(
            0L,
            await CountAsync(
                reading,
                "SELECT count(*) FROM information_schema.columns WHERE table_name = 'encode_job' AND column_name LIKE 'chapters%'"));
        Assert.Equal(
            0L,
            await CountAsync(reading, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_encode_job_chapters'"));
        Assert.Equal(1L, await CountAsync(reading, $"SELECT count(*) FROM encode_job WHERE id = '{job}'"));
    }

    private static async Task SeedAsync(NpgsqlConnection connection)
    {
        await using var seeding = new NpgsqlCommand(
            $"""
            INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
            VALUES ('{Profile}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, timestamptz '2026-09-14 03:00:00+00');
            INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
            VALUES ('{Destination}', 'Primary', 'primary', '{Profile}', timestamptz '2026-09-14 03:00:00+00');
            """,
            connection);

        await seeding.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task JobAsync(NpgsqlConnection connection, Guid job)
    {
        await using var writing = new NpgsqlCommand(
            $"""
            INSERT INTO encode_job (
                id, recording_id, profile_id, destination_id, output_root, status, attempt,
                queued_at, started_at, chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at)
            VALUES (
                '{job}', '{Guid.NewGuid()}', '{Profile}', '{Destination}', 'primary', 'Running', 1,
                timestamptz '2026-09-14 03:00:00+00', timestamptz '2026-09-14 03:00:05+00',
                'Ffmpeg', 'Marked', 0.44, timestamptz '2026-09-14 03:01:00+00')
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task ChapterAsync(
        NpgsqlConnection connection,
        Guid job,
        int ordinal,
        string startsAt,
        string endsAt,
        string kind)
    {
        await using var writing = new NpgsqlCommand(
            $"""
            INSERT INTO encode_chapter (id, job_id, ordinal, starts_at, ends_at, kind)
            VALUES ('{Guid.NewGuid()}', '{job}', {ordinal}, {startsAt}, {endsAt}, '{kind}')
            """,
            connection);

        await writing.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using var reading = new NpgsqlCommand(sql, connection);

        return (long)(await reading.ExecuteScalarAsync(Cancel))!;
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
