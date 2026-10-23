using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class AnswersByHandMigrationTests
{
    private const string ScratchDatabase = "carina_answers_by_hand_test";

    private const string BeforeAnswersWereDropped = "20261019091700_ChaptersOfAnArtefactTellOpeningsAndEndingsApart";

    private const string Queued = "timestamptz '2026-10-19 03:00:00+00'";

    private const string Started = "timestamptz '2026-10-19 03:00:05+00'";

    private const string Made = "timestamptz '2026-10-19 03:30:00+00'";

    private const string Columns = """
        SELECT string_agg(table_name || '.' || column_name, ' ' ORDER BY table_name, column_name)
        FROM information_schema.columns
        WHERE (table_name = 'segment_model_version' AND column_name = 'recommended_threshold')
           OR (table_name = 'encode_job' AND column_name = 'chapters_answered_at')
        """;

    private static readonly Guid Profile = new("8192a3b4-c5d6-4e7f-9809-1a2b3c4d5e6f");

    private static readonly Guid Destination = new("92a3b4c5-d6e7-4f80-919a-2b3c4d5e6f70");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CV-001: the answers, the kind an answer decided, the threshold an evaluation recommended and when the answer an artefact's chapters came from was given are dropped; the entries of an answered recording wait to be estimated again, and going back brings the table and columns back empty")]
    public async Task WhatAnswersWereKeptInIsDropped()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeAnswersWereDropped, Cancel);

        Guid answered = Guid.NewGuid();
        Guid decided = Guid.NewGuid();
        Guid heardThere = Guid.NewGuid();
        Guid heardElsewhere = Guid.NewGuid();
        Guid fromAnAnswer = Guid.NewGuid();
        Guid fromAVersion = Guid.NewGuid();

        await using (NpgsqlConnection before = await OpenAsync())
        {
            await RunAsync(
                before,
                $$"""
                {{Entry(decided, "'Cm'", "1.0", "'Answer'")}};
                {{Entry(heardThere, "'Cm'", "0.9", "'Pattern'")}};
                {{Entry(heardElsewhere, "'Cm'", "0.9", "'Pattern'")}};
                {{Stretch(decided, answered, 600)}};
                {{Stretch(decided, Guid.NewGuid(), 600)}};
                {{Stretch(heardThere, answered, 900)}};
                {{Stretch(heardThere, Guid.NewGuid(), 900)}};
                {{Stretch(heardElsewhere, Guid.NewGuid(), 300)}};
                {{Stretch(heardElsewhere, Guid.NewGuid(), 300)}};
                INSERT INTO segment_answer (recording_id, segments, length, confirmed, confirmed_at, answered_at, answers, withdrawn_at)
                VALUES ('{{answered}}', '[{"startsAtSec": 0, "endsAtSec": 1800, "kind": "Programme"}]', interval '00:30:00', true, {{Made}}, {{Made}}, 1, NULL);
                INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, recommended_threshold, evidence)
                VALUES (90, 'Candidate', false, 2, '{}', '{}', '{}', '{}', '{}', {{Made}}, 0.65, '{"recordings": 12, "boundaries": 148, "labelsUntil": null}');
                INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
                VALUES ('{{Profile}}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, {{Queued}});
                INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
                VALUES ('{{Destination}}', 'Shelf', 'encodes', '{{Profile}}', {{Queued}});
                {{Job(fromAnAnswer, "NULL", Made)}};
                {{Job(fromAVersion, "1", "NULL")}};
                """);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using (NpgsqlConnection forward = await OpenAsync())
        {
            Assert.Equal("", await ScalarAsync(forward, "SELECT coalesce(to_regclass('segment_answer')::text, '')"));
            Assert.Equal("", await ScalarAsync(forward, $"SELECT coalesce(({Columns}), '')"));
            Assert.Equal(
                "Unknown NULL NULL 0",
                await ScalarAsync(forward, $"SELECT concat_ws(' ', kind, coalesce(confidence::text, 'NULL'), coalesce(decided_by, 'NULL'), kinds_version) FROM segment_catalog_entry WHERE id = '{decided}'"));
            Assert.Equal("0", await ScalarAsync(forward, $"SELECT kinds_version::text FROM segment_catalog_entry WHERE id = '{heardThere}'"));
            Assert.Equal("2", await ScalarAsync(forward, $"SELECT kinds_version::text FROM segment_catalog_entry WHERE id = '{heardElsewhere}'"));
            Assert.Equal("NULL", await ScalarAsync(forward, $"SELECT coalesce(chapters_verdict, 'NULL') FROM encode_job WHERE id = '{fromAnAnswer}'"));
            Assert.Equal("Segments Marked 1", await ScalarAsync(forward, $"SELECT concat_ws(' ', chapters_detector, chapters_verdict, chapters_model_version) FROM encode_job WHERE id = '{fromAVersion}'"));
            Assert.Equal("23514", await RefusedAsync(forward, $"UPDATE segment_catalog_entry SET decided_by = 'Answer' WHERE id = '{heardElsewhere}'"));
            Assert.Equal(
                "23514",
                await RefusedAsync(forward, $"UPDATE encode_job SET chapters_model_version = NULL WHERE id = '{fromAVersion}'"));
        }

        await migrator.MigrateAsync(BeforeAnswersWereDropped, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal("0", await ScalarAsync(back, "SELECT count(*)::text FROM segment_answer"));
            Assert.Equal("encode_job.chapters_answered_at segment_model_version.recommended_threshold", await ScalarAsync(back, Columns));
            Assert.Equal("NULL", await ScalarAsync(back, "SELECT coalesce(recommended_threshold::text, 'NULL') FROM segment_model_version WHERE number = 90"));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);
        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static string Entry(Guid id, string kind, string confidence, string decidedBy)
        => $"""
            INSERT INTO segment_catalog_entry (id, kind, confidence, decided_by, length, fingerprint, occurrences, series_count, station_count, first_seen_at, last_seen_at, updated_at, kinds_version)
            VALUES ('{id}', {kind}, {confidence}, {decidedBy}, interval '15 seconds', '\x01020304'::bytea, 2, 2, 2, {Queued}, {Queued}, {Queued}, 2)
            """;

    private static string Stretch(Guid entry, Guid recording, int at)
        => $"""
            INSERT INTO segment_catalog_occurrence (entry_id, recording_id, starts_at, ends_at, network_id, service_id, series_name, recording_started_at, bit_error_rate, kind_override, matched_at)
            VALUES ('{entry}', '{recording}', interval '{at} seconds', interval '{at + 15} seconds', 1, 1, NULL, {Queued}, 0.1, NULL, {Queued})
            """;

    private static string Job(Guid job, string modelVersion, string answeredAt)
    {
        Guid recording = Guid.NewGuid();

        return $"""
            INSERT INTO encode_job (
                id, recording_id, profile_id, destination_id, output_root, status, attempt,
                queued_at, started_at, ended_at, artefact_name,
                chapters_detector, chapters_verdict, chapters_break_share, chapters_decided_at, chapters_model_version, chapters_answered_at)
            VALUES
                ('{job}', '{recording}', '{Profile}', '{Destination}', 'encodes', 'Completed', 1,
                 {Queued}, {Started}, {Made}, '{recording:N}.{Profile:N}.mp4', 'Segments', 'Marked', 0.2, {Made}, {modelVersion}, {answeredAt})
            """;
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new(sql, connection);

        return (string?)await reading.ExecuteScalarAsync(Cancel);
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
