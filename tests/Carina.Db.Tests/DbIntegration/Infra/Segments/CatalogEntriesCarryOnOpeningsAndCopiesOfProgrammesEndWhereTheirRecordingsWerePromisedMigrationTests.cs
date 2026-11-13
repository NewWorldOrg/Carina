using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class CatalogContinuationMigrationTests
{
    private const string ScratchDatabase = "carina_catalog_continuation_test";

    private const string BeforeContinuations = "20261020013101_AVersionMadeByHandHearsSilenceBelowThePausesInAdverts";

    private static readonly DateTime Noon = new(2026, 10, 9, 14, 0, 0, DateTimeKind.Utc);

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact(DisplayName = "BR-CD-005: a copy of a programme whose end the guide did not give ends where its recording was promised to, while the recording is kept; going back keeps the ends and takes entries carried on for ones not known")]
    public async Task ACopyEndsWhereItsRecordingWasPromised()
    {
        await using CarinaDbContext context = CarinaDbContextFactory.Create(Scratch());
        await context.Database.EnsureDeletedAsync(Cancel);
        IMigrator migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync(BeforeContinuations, Cancel);
        Recording unended = Begun(Noon, Noon.AddMinutes(30));
        Recording guided = Begun(Noon.AddDays(7), Noon.AddDays(7).AddMinutes(30));
        RecordingId gone = RecordingId.New();

        await using (CarinaDbContext before = CarinaDbContextFactory.Create(Scratch()))
        {
            before.AddRange(unended, guided);
            before.AddRange(
                LearningExtraction.Waiting(unended.Id, ProgrammeCopy.Of(unended, null), Noon),
                LearningExtraction.Waiting(guided.Id, ProgrammeCopy.Of(guided, Noon.AddDays(7).AddMinutes(25)), Noon),
                LearningExtraction.Waiting(gone, ProgrammeCopy.Of(Begun(Noon, Noon.AddMinutes(30)), null), Noon));
            await before.SaveChangesAsync(Cancel);
        }

        await migrator.MigrateAsync(cancellationToken: Cancel);
        Guid carried = Guid.NewGuid();

        await using (NpgsqlConnection after = await OpenAsync())
        {
            Assert.Equal("2026-10-09T14:30:00Z", await EndAsync(after, unended.Id));
            Assert.Equal("2026-10-16T14:25:00Z", await EndAsync(after, guided.Id));
            Assert.Equal(string.Empty, await EndAsync(after, gone));

            await ExecuteAsync(after, Entry(carried, "Opening", "0.8", "'Continuation'", CatalogKinds.Version));
        }

        await migrator.MigrateAsync(BeforeContinuations, Cancel);

        await using (NpgsqlConnection back = await OpenAsync())
        {
            Assert.Equal(
                "Unknown||0",
                await ScalarAsync(back, $"SELECT kind || '|' || coalesce(decided_by, '') || '|' || coalesce(confidence::text, '') || kinds_version FROM segment_catalog_entry WHERE id = '{carried}'"));
            Assert.Equal("2026-10-09T14:30:00Z", await EndAsync(back, unended.Id));
            await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(back, Entry(Guid.NewGuid(), "Opening", "0.8", "'Continuation'", CatalogKinds.FirstVersion)));
        }

        await context.Database.EnsureDeletedAsync(Cancel);
    }

    private static Recording Begun(DateTime starts, DateTime promisedEnd)
    {
        RecordingId id = RecordingId.New();

        return Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(32738), new ServiceId(1040), new EventId(starts.DayOfYear), starts),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            starts.AddSeconds(-10),
            promisedEnd,
            new ProgrammeSnapshot("架空の物語", string.Empty, string.Empty, [], starts, AudioMode.Stereo, ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            starts.AddSeconds(-10),
            new TunerDeviceId("pt3-0"));
    }

    private static string Entry(Guid id, string kind, string confidence, string decidedBy, int kindsVersion)
        => $"""
           INSERT INTO segment_catalog_entry (id, kind, confidence, decided_by, length, fingerprint, occurrences, series_count, station_count, first_seen_at, last_seen_at, updated_at, kinds_version)
           VALUES ('{id}', '{kind}', {confidence}, {decidedBy}, interval '13 seconds', '\x01020304'::bytea, 2, 1, 1, timestamptz '2026-10-09T14:00:00Z', timestamptz '2026-10-16T14:00:00Z', timestamptz '2026-10-16T14:00:00Z', {kindsVersion})
           """;

    private static Task<string> EndAsync(NpgsqlConnection connection, RecordingId recording)
        => ScalarAsync(
            connection,
            $"SELECT coalesce(to_char(programme_ends_at AT TIME ZONE 'UTC', 'YYYY-MM-DD\"T\"HH24:MI:SS\"Z\"'), '') FROM segment_extraction WHERE recording_id = '{recording.Value}'");

    private static string Scratch()
    {
        string connection = Environment.GetEnvironmentVariable(CarinaDbContextFactory.ConnectionStringVariable)
                            ?? throw new InvalidOperationException($"{CarinaDbContextFactory.ConnectionStringVariable} names the database the tests make theirs beside.");

        return new NpgsqlConnectionStringBuilder(connection) { Database = ScratchDatabase }.ConnectionString;
    }

    private static async Task<NpgsqlConnection> OpenAsync()
    {
        NpgsqlConnection connection = new(Scratch());
        await connection.OpenAsync(Cancel);

        return connection;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand reading = new(sql, connection);

        return Convert.ToString(await reading.ExecuteScalarAsync(Cancel), CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
