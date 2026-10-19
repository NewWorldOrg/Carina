using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class EncodeChapterKindsMigrationTests
{
    private const string ScratchDatabase = "carina_encode_chapter_kinds_test";

    private const string BeforeOpeningsAndEndings = "20261018224946_AVersionMadeByHandTellsOpeningsAndEndingsApart";

    private const string Queued = "timestamptz '2026-10-19 03:00:00+00'";

    private const string Started = "timestamptz '2026-10-19 03:00:05+00'";

    private const string Made = "timestamptz '2026-10-19 03:30:00+00'";

    private static readonly Guid Profile = new("6f708192-a3b4-4c5d-96e7-f8091a2b3c4d");

    private static readonly Guid Destination = new("708192a3-b4c5-4d6e-87f8-091a2b3c4d5e");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-010: chapters kept as the programme and a break stay as they were, an opening and an ending are kept once the migration is in, going back reads them as a break, and forward again")]
    public async Task ChaptersOfAnOpeningAndAnEndingAreKeptAndGoBackAsABreak()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeOpeningsAndEndings, Cancel);

        Guid job = Guid.NewGuid();
        Guid recording = Guid.NewGuid();

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await RunAsync(
                before,
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
                    ('{job}', '{recording}', '{Profile}', '{Destination}', 'encodes', 'Completed', 1,
                     {Queued}, {Started}, {Made}, '{recording:N}.{Profile:N}.mp4', 'Segments', 'Marked', 0.2, {Made}, 1);
                INSERT INTO encode_chapter (id, job_id, ordinal, starts_at, ends_at, kind)
                VALUES
                    ('{Guid.NewGuid()}', '{job}', 0, interval '0', interval '00:05:00', 'Programme'),
                    ('{Guid.NewGuid()}', '{job}', 1, interval '00:05:00', interval '00:06:00', 'Break'),
                    ('{Guid.NewGuid()}', '{job}', 2, interval '00:06:00', interval '00:30:00', 'Programme');
                """);
            Assert.Equal("23514", await RefusedAsync(before, Chapter(job, 3, "Opening")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync())
        {
            Assert.Equal("Programme Break Programme", await KindsAsync(forward, job));
            await RunAsync(forward, $"DELETE FROM encode_chapter WHERE job_id = '{job}'");
            await RunAsync(
                forward,
                $"""
                {Chapter(job, 0, "Opening", "interval '0'", "interval '00:01:30'")};
                {Chapter(job, 1, "Programme", "interval '00:01:30'", "interval '00:28:30'")};
                {Chapter(job, 2, "Ending", "interval '00:28:30'", "interval '00:30:00'")};
                """);
            Assert.Equal("Opening Programme Ending", await KindsAsync(forward, job));
            Assert.Equal("23514", await RefusedAsync(forward, Chapter(job, 3, "Advertisement")));
        }

        await migrator.MigrateAsync(BeforeOpeningsAndEndings, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("Break Programme Break", await KindsAsync(back, job));
            Assert.Equal("23514", await RefusedAsync(back, Chapter(job, 3, "Ending")));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection again = await OpenAsync())
        {
            Assert.Equal("Break Programme Break", await KindsAsync(again, job));
            await RunAsync(again, Chapter(job, 3, "Ending"));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Chapter(Guid job, int ordinal, string kind, string startsAt = "interval '00:40:00'", string endsAt = "interval '00:41:00'")
        => $"INSERT INTO encode_chapter (id, job_id, ordinal, starts_at, ends_at, kind) VALUES ('{Guid.NewGuid()}', '{job}', {ordinal}, {startsAt}, {endsAt}, '{kind}')";

    private static async Task<string> KindsAsync(NpgsqlConnection connection, Guid job)
    {
        await using NpgsqlCommand reading = new($"SELECT string_agg(kind, ' ' ORDER BY ordinal) FROM encode_chapter WHERE job_id = '{job}'", connection);

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
