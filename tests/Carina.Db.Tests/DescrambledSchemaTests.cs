using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class DescrambledSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    [Fact]
    public async Task ARecordingLeftScrambledIsReadBackDescrambledOnceItWas()
    {
        Recording ended = await EndedAsync(RecordingFault.ScramblingUnresolved);

        await using (CarinaDbContext writing = Context())
        {
            Recording held = await writing.Set<Recording>().SingleAsync(recording => recording.Id == ended.Id);
            held.Descrambled(Noon.AddDays(1));
            await writing.SaveChangesAsync();
        }

        await using CarinaDbContext reading = Context();
        Recording again = await reading.Set<Recording>().SingleAsync(recording => recording.Id == ended.Id);

        Assert.Equal(Noon.AddDays(1), again.DescrambledAt);
        Assert.False(again.LeftScrambled);
    }

    [Fact]
    public async Task TheTableRefusesARecordingDescrambledThatNeverNamedTheScrambling()
    {
        Recording ended = await EndedAsync(RecordingFault.DriverLost);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => UpdateRecordingAsync(ended, "timestamptz '2026-08-26 12:00:00+00'"));

        Assert.Equal("ck_recording_descrambled", refused.ConstraintName);
    }

    [Fact]
    public async Task TheTableRefusesARecordingDescrambledBeforeItStopped()
    {
        Recording ended = await EndedAsync(RecordingFault.ScramblingUnresolved);

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => UpdateRecordingAsync(ended, "timestamptz '2026-08-24 12:30:00+00'"));

        Assert.Equal("ck_recording_descrambled", refused.ConstraintName);
    }

    [Fact]
    public async Task TheLedgerTakesALineWhoseRecordingCameOutWholeButScrambled()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await LineAsync(connection, "'Complete'", "'[\"ScramblingUnresolved\"]'::jsonb", "NULL");
        await LineAsync(connection, "'Complete'", "'[\"ScramblingUnresolved\"]'::jsonb", Ends);
    }

    [Fact]
    public async Task TheLedgerRefusesALineWhoseRecordingCameOutWholeAndNamesNoScrambling()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => LineAsync(connection, "'Complete'", "'[\"HeavierThanTheStream\"]'::jsonb", "NULL"));

        Assert.Equal("ck_reservation_outcome_whole_but_scrambled", refused.ConstraintName);
    }

    [Fact]
    public async Task TheLedgerRefusesALineDescrambledThatNeverNamedTheScrambling()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => LineAsync(connection, "'Truncated'", "'[\"ShortOfTheWindow\"]'::jsonb", Ends));

        Assert.Equal("ck_reservation_outcome_descrambled", refused.ConstraintName);
    }

    [Fact]
    public async Task TheLedgerRefusesALineDescrambledBeforeItWasWritten()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => LineAsync(connection, "'Complete'", "'[\"ScramblingUnresolved\"]'::jsonb", Airs));

        Assert.Equal("ck_reservation_outcome_descrambled", refused.ConstraintName);
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task UpdateRecordingAsync(Recording recording, string descrambledAt)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(
            $"UPDATE recording SET descrambled_at = {descrambledAt} WHERE id = @id",
            connection);
        command.Parameters.AddWithValue("id", recording.Id.Value);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task LineAsync(
        NpgsqlConnection connection,
        string recordingOutcome,
        string faults,
        string descrambledAt)
    {
        await using var command = new NpgsqlCommand(
            $"""
            INSERT INTO reservation_outcome (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                snapshot_name, effective_start_at, effective_end_at, priority, rule_id,
                kind, tune_failure, recording_outcome, faults, recorded_instead, occurred_at,
                retry_result, gave_up_because, descrambled_at)
            VALUES (
                '{Guid.NewGuid()}', '{Guid.NewGuid()}', 47101, 1024, 4001, {Airs},
                'A programme', {Airs}, {Ends}, 50, NULL,
                '{ReservationOutcomeKind.RecordingFailure}', NULL, {recordingOutcome}, {faults}, '[]'::jsonb, {Ends},
                NULL, NULL, {descrambledAt})
            """,
            connection);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<Recording> EndedAsync(RecordingFault fault)
    {
        RecordingId id = RecordingId.New();

        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(47102), new ServiceId(1024), new EventId(4001), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot(
                "A programme",
                string.Empty,
                string.Empty,
                [],
                Noon,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            new TunerDeviceId("pt3-0"));

        recording.Wrote(TimeSpan.FromHours(1));
        recording.Note(new OutcomeDetail(fault, null, string.Empty, Noon));
        recording.Abort(Noon.AddHours(1));
        recording.Settle(RecordingOutcome.Complete, 1_000, Noon.AddHours(1));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
