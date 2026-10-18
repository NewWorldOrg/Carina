using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SegmentChaptersRollbackTests
{
    private const string ScratchDatabase = "carina_segment_chapters_rollback_test";

    private const string BeforeChaptersCameFromTheSegments = "20261018083305_TheDetectionIsKeptPerVersionOfTheModelWithoutAKeyIntoTheRecording";

    private const string Queued = "timestamptz '2026-10-18 03:00:00+00'";

    private const string Started = "timestamptz '2026-10-18 03:00:05+00'";

    private const string Made = "timestamptz '2026-10-18 03:30:00+00'";

    private const string Rewritten = "timestamptz '2026-10-18 05:00:00+00'";

    private static readonly Guid Profile = new("4d5e6f70-8192-4a3b-b4c5-d6e7f8091a2b");

    private static readonly Guid Destination = new("5e6f7081-92a3-4b4c-85d6-e7f8091a2b3c");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-010: a ledger whose chapters came from the segments goes back to before they could, keeping every job with its chapters unread and letting go of the copies, and forward again")]
    public async Task ALedgerWhoseChaptersCameFromTheSegmentsGoesBackAndForwardAgain()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        Guid recording = Guid.NewGuid();
        Guid segments = Guid.NewGuid();
        Guid looked = Guid.NewGuid();

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await RunAsync(
                writing,
                $"""
                INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
                VALUES ('{Profile}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, {Queued});
                INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
                VALUES ('{Destination}', 'Shelf', 'encodes', '{Profile}', {Queued});
                INSERT INTO encode_job (
                    id, recording_id, profile_id, destination_id, output_root, status, attempt,
                    queued_at, started_at, ended_at, artefact_name,
                    chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at, chapters_model_version)
                VALUES
                    ('{segments}', '{recording}', '{Profile}', '{Destination}', 'encodes', 'Completed', 1,
                     {Queued}, {Started}, {Made}, '{recording:N}.{Profile:N}.mp4', 'Segments', 'Marked', 0.2, {Rewritten}, 1),
                    ('{looked}', '{Guid.NewGuid()}', '{Profile}', '{Destination}', 'encodes', 'Running', 1,
                     {Queued}, {Started}, NULL, NULL, 'Ffmpeg', 'Marked', 0.3, {Started}, NULL);
                INSERT INTO encode_scratch_file (id, job_id, kind, output_root, file_name, written_at, removed_at, fate)
                VALUES
                    ('{Guid.NewGuid()}', '{segments}', 'ChapteredWork', 'encodes', '{recording:N}.{segments:N}.attempt1.chaptered', {Rewritten}, {Rewritten}, 'BecameTheArtefact'),
                    ('{Guid.NewGuid()}', '{segments}', 'Chapters', 'encodes', '{recording:N}.{segments:N}.attempt1.chapters', {Rewritten}, {Rewritten}, 'Removed');
                """);
        }

        await migrator.MigrateAsync(BeforeChaptersCameFromTheSegments, Cancel);

        await using (NpgsqlConnection reading = await OpenAsync())
        {
            Assert.Equal(0L, await CountAsync(reading, ModelVersionColumn));
            Assert.Equal(0L, await CountAsync(reading, "SELECT count(*) FROM encode_scratch_file WHERE kind = 'ChapteredWork'"));
            Assert.Equal(1L, await CountAsync(reading, "SELECT count(*) FROM encode_scratch_file WHERE kind = 'Chapters'"));
            Assert.Equal(1L, await CountAsync(reading, $"SELECT count(*) FROM encode_job WHERE id = '{segments}' AND chapters_detector IS NULL AND chapters_verdict IS NULL"));
            Assert.Equal(1L, await CountAsync(reading, $"SELECT count(*) FROM encode_job WHERE id = '{looked}' AND chapters_detector = 'Ffmpeg'"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection forward = await OpenAsync();

        Assert.Equal(1L, await CountAsync(forward, ModelVersionColumn));
        Assert.Equal(2L, await CountAsync(forward, "SELECT count(*) FROM encode_job"));
    }

    private const string ModelVersionColumn =
        "SELECT count(*) FROM information_schema.columns WHERE table_name = 'encode_job' AND column_name = 'chapters_model_version'";

    private static async Task RunAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand running = new NpgsqlCommand(sql, connection);

        await running.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new NpgsqlCommand(sql, connection);

        return (long)(await reading.ExecuteScalarAsync(Cancel))!;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new NpgsqlConnection(Scratch());
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
