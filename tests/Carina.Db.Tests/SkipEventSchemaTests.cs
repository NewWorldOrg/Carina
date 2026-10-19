using System.Globalization;

using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class SkipEventSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private int nextEvent = 8_600;

    [Fact(DisplayName = "BR-CD-002: the skip events of a recording outlive the recording")]
    public async Task TheSkipEventsOutliveTheRecording()
    {
        Recording recording = await RecordedAsync();
        Assert.True(await TakeAsync(Skip(recording.Id, Noon, 1)));

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var erasing = new NpgsqlCommand("DELETE FROM recording WHERE id = @recording", connection);
            erasing.Parameters.AddWithValue("recording", recording.Id.Value);
            Assert.Equal(1, await erasing.ExecuteNonQueryAsync());
        }

        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM segment_skip_event WHERE recording_id = '{recording.Id.Value}'"));
    }

    [Fact(DisplayName = "BR-CV-003: a skip event is kept with the kind and action by name, the moments as intervals and the version, and with nothing of who did it")]
    public async Task ASkipEventIsKept()
    {
        RecordingId recording = RecordingId.New();
        RecordingId returned = RecordingId.New();

        Assert.True(await TakeAsync(Skip(recording, Noon, 1)));
        Assert.True(await TakeAsync(SkipEvent.Of(returned, new ViewerAction(Noon, ViewerActionKind.Returned, SegmentKind.Ending, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(12.5), null))));

        Assert.Equal(
            "Skipped Break 00:15:15 00:16:42.3 1",
            await ScalarAsync($"SELECT concat_ws(' ', event, kind, from_at, to_at, model_version) FROM segment_skip_event WHERE recording_id = '{recording.Value}'"));
        Assert.Equal(
            "Returned Ending NULL",
            await ScalarAsync($"SELECT concat_ws(' ', event, kind, coalesce(model_version::text, 'NULL')) FROM segment_skip_event WHERE recording_id = '{returned.Value}'"));
        Assert.Equal(
            "event from_at happened_at id kind model_version recording_id to_at",
            await ScalarAsync("SELECT string_agg(column_name, ' ' ORDER BY column_name) FROM information_schema.columns WHERE table_name = 'segment_skip_event'"));
    }

    [Fact(DisplayName = "BR-CV-003: six hundred events of a recording are taken within an hour and the next is not, while another recording and the hour after are unaffected")]
    public async Task SixHundredEventsAreTakenWithinAnHour()
    {
        RecordingId recording = RecordingId.New();
        RecordingId other = RecordingId.New();
        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(
                connection,
                $"""
                INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version)
                SELECT '{recording.Value}', timestamptz '2026-10-18T11:00:01Z' + make_interval(secs => n), 'Skipped', 'Break', interval '915 seconds', interval '1002 seconds', 1
                FROM generate_series(1, 599) AS n
                """);
        }

        Assert.True(await TakeAsync(Skip(recording, Noon, 1)));
        Assert.False(await TakeAsync(Skip(recording, Noon.AddSeconds(1), 1)));
        Assert.True(await TakeAsync(Skip(other, Noon.AddSeconds(1), 1)));
        Assert.True(await TakeAsync(Skip(recording, Noon.AddHours(1).AddSeconds(1), 1)));
        Assert.Equal("601", await ScalarAsync($"SELECT count(*) FROM segment_skip_event WHERE recording_id = '{recording.Value}'"));
    }

    [Fact(DisplayName = "a version of the model is never removed while a skip event names it, and a skip event naming no version kept is refused")]
    public async Task AVersionNamedByASkipEventIsNotRemoved()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(
            connection,
            """
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, retired_at)
            VALUES (41, 'Retired', false, 2, '{}', '{}', '{}', '{}', '{}', now(), now(), now())
            """);
        await ExecuteAsync(connection, InsertSkip("41"));
        await ExecuteAsync(connection, "SAVEPOINT named");

        PostgresException restricted = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "DELETE FROM segment_model_version WHERE number = 41"));
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT named");
        PostgresException unknown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, InsertSkip("42")));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, restricted.SqlState);
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, unknown.SqlState);

        await transaction.RollbackAsync();
    }

    [Theory(DisplayName = "the skip event table refuses an action it does not know, the programme as what a button offered, and a moment before the recording's zero")]
    [InlineData("event", "'Watched'", "ck_segment_skip_event_event")]
    [InlineData("kind", "'Programme'", "ck_segment_skip_event_kind")]
    [InlineData("from_at", "interval '-1 second'", "ck_segment_skip_event_moments")]
    [InlineData("to_at", "interval '-1 second'", "ck_segment_skip_event_moments")]
    public async Task TheSkipEventTableRefusesWhatDoesNotHold(string column, string value, string constraint)
    {
        Dictionary<string, string> row = new(StringComparer.Ordinal)
        {
            ["recording_id"] = $"'{Guid.NewGuid()}'",
            ["happened_at"] = "now()",
            ["event"] = "'Skipped'",
            ["kind"] = "'Break'",
            ["from_at"] = "interval '915 seconds'",
            ["to_at"] = "interval '1002 seconds'",
            ["model_version"] = "1",
        };
        row[column] = value;
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connection, $"INSERT INTO segment_skip_event ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [Fact(DisplayName = "the events are found by recording and moment, by moment alone and by version")]
    public async Task TheIndexesAreTheOnesReadBy()
    {
        Assert.Equal(
            "(happened_at) (model_version) (recording_id, happened_at)",
            await ScalarAsync(
                """
                SELECT string_agg(substring(indexdef FROM '\(.*\)'), ' ' ORDER BY indexdef)
                FROM pg_indexes WHERE tablename = 'segment_skip_event' AND indexname <> 'pk_segment_skip_event'
                """));
    }

    [Fact(DisplayName = "BR-CD-006: learning reads the skip events of the recordings it names, the earliest first, and nothing of other recordings")]
    public async Task LearningReadsEveryEvent()
    {
        RecordingId viewed = RecordingId.New();
        await TakeAsync(Skip(viewed, Noon.AddMinutes(9), null));
        await TakeAsync(Skip(viewed, Noon.AddMinutes(3), SegmentModelVersion.FirstNumber));
        RecordingId other = RecordingId.New();
        await TakeAsync(Skip(other, Noon.AddMinutes(4), null));

        await using CarinaDbContext context = Context();
        IReadOnlyList<SkipEvent> events = await new SkipEventRepository(context).ListAsync([viewed, RecordingId.New()], CancellationToken.None);

        Assert.Equal(
            [(Noon.AddMinutes(3), (int?)SegmentModelVersion.FirstNumber), (Noon.AddMinutes(9), null)],
            events.Select(skip => (skip.HappenedAt, skip.ModelVersion)));
    }

    private static string InsertSkip(string version)
        => $"""
            INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version)
            VALUES ('{Guid.NewGuid()}', now(), 'Skipped', 'Break', interval '915 seconds', interval '1002 seconds', {version})
            """;

    private static SkipEvent Skip(RecordingId recording, DateTime at, int? version)
        => SkipEvent.Of(recording, new ViewerAction(at, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002.3), version));

    private static async Task ExecuteAsync(NpgsqlConnection connection, string statement)
    {
        await using var command = new NpgsqlCommand(statement, connection);

        await command.ExecuteNonQueryAsync();
    }

    private async Task<string?> ScalarAsync(string query)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(query, connection);

        return Convert.ToString(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<bool> TakeAsync(SkipEvent skip)
    {
        await using CarinaDbContext context = Context();

        return await new SkipEventRepository(context).TakeAsync(skip, CancellationToken.None);
    }

    private async Task<Recording> RecordedAsync()
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_002), new ServiceId(60_002), new EventId(nextEvent++), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot("架空の番組", "架空のあらすじ", string.Empty, [new ProgrammeGenre(7, 0)], Noon, AudioMode.Stereo, ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon);

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 3_400_000, Noon.AddMinutes(30));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
