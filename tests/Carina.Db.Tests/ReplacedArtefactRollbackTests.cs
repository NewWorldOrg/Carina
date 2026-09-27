using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReplacedArtefactRollbackTests
{
    private const string ScratchDatabase = "carina_replaced_artefact_rollback_test";

    private const string BeforeArtefactsWereReplaced = "20260927051252_WhatWasLeftScrambledCanBeLifted";

    private const string Queued = "timestamptz '2026-09-27 03:00:00+00'";

    private const string Started = "timestamptz '2026-09-27 03:00:05+00'";

    private const string MadeFirst = "timestamptz '2026-09-27 03:30:00+00'";

    private const string MadeAgain = "timestamptz '2026-09-27 04:30:00+00'";

    private const string Replaced = "timestamptz '2026-09-27 04:31:00+00'";

    private static readonly Guid First = new("1a2b3c4d-5e6f-4071-8293-a4b5c6d7e8f9");

    private static readonly Guid Second = new("2b3c4d5e-6f70-4182-93a4-b5c6d7e8f901");

    private static readonly Guid Destination = new("3c4d5e6f-7081-4293-a4b5-c6d7e8f90123");

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "a ledger that has replaced artefacts goes back to before they were replaced and forward again, keeping every job and work file and letting go of the replaced artefacts' removals")]
    public async Task ALedgerThatHasReplacedArtefactsGoesBackAndForwardAgain()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);

        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(cancellationToken: Cancel);

        Guid recording = Guid.NewGuid();
        Guid replaced = Guid.NewGuid();
        Guid again = Guid.NewGuid();
        Guid running = Guid.NewGuid();
        string replacedName = $"{recording:N}.{First:N}.mp4";

        await using (NpgsqlConnection writing = await OpenAsync())
        {
            await RunAsync(
                writing,
                $"""
                INSERT INTO encode_profile (id, label, codec, resolution, deinterlace, rate_factor, quantiser, defined_at)
                VALUES ('{First}', 'Viewing', 'H264', 'AsSource', 'Leave', 23, 24, {Queued}),
                       ('{Second}', 'Keeping', 'H265', 'AsSource', 'Leave', 23, 24, {Queued});
                INSERT INTO encode_destination (id, label, output_root, default_profile_id, defined_at)
                VALUES ('{Destination}', 'Shelf', 'encodes', '{First}', {Queued});
                INSERT INTO encode_job (
                    id, recording_id, profile_id, destination_id, output_root, status, attempt,
                    queued_at, started_at, ended_at, artefact_name, replaced_at)
                VALUES
                    ('{replaced}', '{recording}', '{First}', '{Destination}', 'encodes', 'Completed', 1,
                     {Queued}, {Started}, {MadeFirst}, '{replacedName}', {Replaced}),
                    ('{again}', '{recording}', '{Second}', '{Destination}', 'encodes', 'Completed', 1,
                     {Queued}, {Started}, {MadeAgain}, '{recording:N}.{Second:N}.mp4', NULL),
                    ('{running}', '{Guid.NewGuid()}', '{First}', '{Destination}', 'encodes', 'Running', 1,
                     {Queued}, {Started}, NULL, NULL, NULL);
                INSERT INTO encode_scratch_file (id, job_id, kind, output_root, file_name, written_at, removed_at, fate)
                VALUES
                    ('{Guid.NewGuid()}', '{replaced}', 'ReplacedArtefact', 'encodes', '{replacedName}', {Replaced}, {Replaced}, 'Removed'),
                    ('{Guid.NewGuid()}', '{again}', 'ReplacedArtefact', 'encodes', '{replacedName}', {Replaced}, {Replaced}, 'CouldNotBeRemoved'),
                    ('{Guid.NewGuid()}', '{running}', 'WorkFile', 'encodes', '{recording:N}.{running:N}.attempt1.encoding', {Started}, NULL, NULL);
                """);
        }

        await migrator.MigrateAsync(BeforeArtefactsWereReplaced, Cancel);

        await using (NpgsqlConnection reading = await OpenAsync())
        {
            Assert.Equal(0L, await CountAsync(reading, ReplacedAtColumn));
            Assert.Equal(0L, await CountAsync(reading, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_encode_job_replaced'"));
            Assert.Equal(0L, await CountAsync(reading, "SELECT count(*) FROM encode_scratch_file WHERE kind = 'ReplacedArtefact'"));
            Assert.Equal(1L, await CountAsync(reading, $"SELECT count(*) FROM encode_scratch_file WHERE job_id = '{running}' AND kind = 'WorkFile'"));
            Assert.Equal(3L, await CountAsync(reading, "SELECT count(*) FROM encode_job"));
            Assert.Equal(
                "CREATE UNIQUE INDEX ux_encode_scratch_file_name ON public.encode_scratch_file USING btree (output_root, file_name)",
                await IndexDefinitionAsync(reading));
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);

        await using NpgsqlConnection forward = await OpenAsync();

        Assert.Equal(1L, await CountAsync(forward, ReplacedAtColumn));
        Assert.Equal(1L, await CountAsync(forward, "SELECT count(*) FROM pg_constraint WHERE conname = 'ck_encode_job_replaced'"));
        Assert.Equal(0L, await CountAsync(forward, "SELECT count(*) FROM encode_job WHERE replaced_at IS NOT NULL"));
        Assert.Equal(
            "CREATE UNIQUE INDEX ux_encode_scratch_file_name ON public.encode_scratch_file USING btree (output_root, file_name) "
            + "WHERE ((kind)::text <> 'ReplacedArtefact'::text)",
            await IndexDefinitionAsync(forward));
    }

    private const string ReplacedAtColumn =
        "SELECT count(*) FROM information_schema.columns WHERE table_name = 'encode_job' AND column_name = 'replaced_at'";

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

    private static async Task<string> IndexDefinitionAsync(NpgsqlConnection connection)
    {
        await using NpgsqlCommand reading = new NpgsqlCommand(
            "SELECT pg_get_indexdef('ux_encode_scratch_file_name'::regclass)",
            connection);

        return (string)(await reading.ExecuteScalarAsync(Cancel))!;
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
