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
public sealed class SegmentDetectionSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private int nextEvent = 8_100;

    [Fact(DisplayName = "BR-CS-003: the first version is in use from the start, holding the first model and no figures of an evaluation")]
    public async Task TheFirstVersionIsInUseFromTheStart()
    {
        SegmentModelVersion active = await ActiveAsync();

        Assert.Equal(SegmentModelVersion.FirstNumber, active.Number);
        Assert.Equal(SegmentModelVersionState.Active, active.State);
        Assert.False(active.ByHand);
        Assert.Equal(2, active.Kinds);
        Assert.Equal(SegmentModel.First, active.Model);
        Assert.Equal(active.TrainedAt, active.AdoptedAt);
        Assert.Null(active.RetiredAt);
        Assert.Equal(
            "NULL NULL NULL NULL NULL",
            await ScalarAsync("SELECT concat_ws(' ', coalesce(metrics::text, 'NULL'), coalesce(stations::text, 'NULL'), coalesce(evidence::text, 'NULL'), coalesce(recommended_threshold::text, 'NULL'), coalesce(rollback::text, 'NULL')) FROM segment_model_version WHERE number = 1"));
    }

    [Fact(DisplayName = "BR-CS-003: exactly one version is in use: a second one in use is refused by the unique index, and a candidate beside it is kept")]
    public async Task ExactlyOneVersionIsInUse()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(connection, Version(7, "'Candidate'", "NULL"));
        await ExecuteAsync(connection, "SAVEPOINT before_second");
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, Version(8, "'Active'", "now()")));
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT before_second");

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
        Assert.Equal("ux_segment_model_version_active", refused.ConstraintName);
        Assert.Equal("1", await ScalarAsync(connection, "SELECT count(*) FROM segment_model_version WHERE state = 'Active'"));
        Assert.Equal("2", await ScalarAsync(connection, "SELECT count(*) FROM segment_model_version"));

        await transaction.RollbackAsync();
    }

    [Theory(DisplayName = "the version table refuses a version chosen by hand that is not in use, a candidate put in use, a version in use taken out of it, or a rollback on a version still in use")]
    [InlineData("'Candidate'", "now()", "NULL", "false", "NULL", "ck_segment_model_version_times")]
    [InlineData("'Retired'", "now()", "NULL", "false", "NULL", "ck_segment_model_version_times")]
    [InlineData("'Retired'", "now()", "now()", "true", "NULL", "ck_segment_model_version_by_hand")]
    [InlineData("'Retired'", "now()", "now()", "false", "now()", "ck_segment_model_version_rollback")]
    public async Task TheVersionTableRefusesWhatDoesNotHold(string state, string adoptedAt, string retiredAt, string byHand, string rolledBackAt, string constraint)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        string statement =
            $$"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, retired_at, chosen_by_hand_at, rolled_back_at)
            VALUES (9, {{state}}, {{byHand}}, 2, '{}', '{}', '{}', '{}', '{}', now(), {{adoptedAt}}, {{retiredAt}}, now(), {{rolledBackAt}})
            """;

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, statement));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [Fact(DisplayName = "BR-CS-002: a recording not detected, one where only the programme was found and one with breaks marked are three different answers")]
    public async Task NotDetectedNothingFoundAndMarkedAreDifferentAnswers()
    {
        RecordingId notDetected = RecordingId.New();
        RecordingId plain = RecordingId.New();
        RecordingId withBreaks = RecordingId.New();
        RecordingDetection nothing = Detection(plain, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, []));
        RecordingDetection marked = Detection(
            withBreaks,
            SegmentDetection.Judged(
                [Span(0, 912.3, SegmentKind.Programme, 0.98), Span(912.3, 1002.3, SegmentKind.Break, 0.93), Span(1002.3, 1800, SegmentKind.Programme, 0.99)],
                HalfAnHour,
                [TimeSpan.FromSeconds(912.28), TimeSpan.FromSeconds(912.31), TimeSpan.FromSeconds(1002.3)]));

        await KeepAsync(nothing);
        await KeepAsync(marked);

        Assert.Null(await FindAsync(notDetected, 1));
        RecordingDetection keptNothing = (await FindAsync(plain, 1))!;
        RecordingDetection keptMarked = (await FindAsync(withBreaks, 1))!;
        Assert.Equal(SegmentDetectionState.NothingFound, keptNothing.State);
        Assert.Equal(0, keptNothing.CmShare);
        Assert.Equal(nothing.Segments, keptNothing.Segments);
        Assert.Equal(SegmentDetectionState.Marked, keptMarked.State);
        Assert.Equal(marked.Segments, keptMarked.Segments);
        Assert.Equal(90d / 1800, keptMarked.CmShare, 12);
        Assert.Equal(marked.Snaps, keptMarked.Snaps);
        Assert.Equal((2, Noon.AddMinutes(-1), Noon), (keptMarked.ExtractionVersionNumber, keptMarked.CatalogUntil, keptMarked.DetectedAt));
        Assert.Equal(
            "t t",
            await ScalarAsync(
                $$"""
                SELECT concat_ws(' ',
                    segments = '[{"startsAtSec": 0, "endsAtSec": 912.3, "kind": "Programme", "probability": 0.98}, {"startsAtSec": 912.3, "endsAtSec": 1002.3, "kind": "Break", "probability": 0.93}, {"startsAtSec": 1002.3, "endsAtSec": 1800, "kind": "Programme", "probability": 0.99}]'::jsonb,
                    snaps = '[912.28, 912.31, 1002.3]'::jsonb)
                FROM segment_detection WHERE recording_id = '{{withBreaks.Value}}'
                """));
    }

    [Fact(DisplayName = "BR-CS-002: a sequence thrown away is kept as it was, with the share breaks took")]
    public async Task ASequenceThrownAwayIsKept()
    {
        RecordingId recording = RecordingId.New();

        await KeepAsync(Detection(recording, SegmentDetection.Judged([Span(0, 600, SegmentKind.Programme, 0.6), Span(600, 1800, SegmentKind.Break, 0.8)], HalfAnHour, [])));

        RecordingDetection kept = (await FindAsync(recording, 1))!;
        Assert.Equal(SegmentDetectionState.Discarded, kept.State);
        Assert.Equal(2, kept.Segments.Count);
        Assert.Equal(1200d / 1800, kept.CmShare, 12);
    }

    [Fact(DisplayName = "a detection kept again for the same recording and version takes the place of the one kept, one row a pair, and another version adds its own")]
    public async Task OneRowIsKeptPerRecordingAndVersion()
    {
        RecordingId recording = RecordingId.New();
        await KeepAsync(Detection(recording, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, [])));

        await KeepAsync(Detection(
            recording,
            SegmentDetection.Judged([Span(0, 300, SegmentKind.Programme, 0.9), Span(300, 360, SegmentKind.Break, 0.8), Span(360, 1800, SegmentKind.Programme, 0.9)], HalfAnHour, []),
            at: Noon.AddHours(1)));

        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM segment_detection WHERE recording_id = '{recording.Value}'"));
        RecordingDetection kept = (await FindAsync(recording, 1))!;
        Assert.Equal(SegmentDetectionState.Marked, kept.State);
        Assert.Equal(Noon.AddHours(1), kept.DetectedAt);

        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();
        await ExecuteAsync(connection, Version(2, "'Candidate'", "NULL"));
        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at)
            SELECT recording_id, 2, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at FROM segment_detection WHERE recording_id = '{recording.Value}'
            """);

        Assert.Equal("2", await ScalarAsync(connection, $"SELECT count(*) FROM segment_detection WHERE recording_id = '{recording.Value}'"));

        await transaction.RollbackAsync();
    }

    [Fact(DisplayName = "BR-CD-002: the detections outlive the recording they were made of")]
    public async Task TheDetectionsOutliveTheRecording()
    {
        Recording recording = await RecordedAsync();
        await KeepAsync(Detection(recording.Id, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, [])));

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var erasing = new NpgsqlCommand("DELETE FROM recording WHERE id = @recording", connection);
            erasing.Parameters.AddWithValue("recording", recording.Id.Value);
            Assert.Equal(1, await erasing.ExecuteNonQueryAsync());
        }

        Assert.NotNull(await FindAsync(recording.Id, 1));
    }

    [Fact(DisplayName = "a version of the model is never removed while a detection names it")]
    public async Task AVersionNamedByADetectionIsNotRemoved()
    {
        await KeepAsync(Detection(RecordingId.New(), SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, [])));
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, "DELETE FROM segment_model_version WHERE number = 1"));

        Assert.Equal(PostgresErrorCodes.RestrictViolation, refused.SqlState);
    }

    [Theory(DisplayName = "the detection table refuses a state it does not know, a share outside 0 to 1, and breaks marked in a single stretch")]
    [InlineData("state", "'Answered'", "ck_segment_detection_state")]
    [InlineData("cm_share", "1.5", "ck_segment_detection_cm_share")]
    [InlineData("segments", "'[{\"kind\": \"Break\", \"startsAtSec\": 0, \"endsAtSec\": 1800}]'", "ck_segment_detection_segments")]
    [InlineData("extraction_version", "0", "ck_segment_detection_extraction_version")]
    public async Task TheDetectionTableRefusesWhatDoesNotHold(string column, string value, string constraint)
    {
        Dictionary<string, string> row = new(StringComparer.Ordinal)
        {
            ["recording_id"] = $"'{Guid.NewGuid()}'",
            ["model_version"] = "1",
            ["state"] = "'Marked'",
            ["segments"] = "'[{\"kind\": \"Programme\", \"startsAtSec\": 0, \"endsAtSec\": 900}, {\"kind\": \"Break\", \"startsAtSec\": 900, \"endsAtSec\": 1800}]'",
            ["cm_share"] = "0.5",
            ["snaps"] = "'[]'",
            ["extraction_version"] = "2",
            ["catalog_until"] = "now()",
            ["detected_at"] = "now()",
        };
        row[column] = value;
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connection, $"INSERT INTO segment_detection ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [Fact(DisplayName = "the recordings waiting to be detected are the ones still kept, matched since they last changed and not detected by the version since, the most recently started first; one matched again waits again")]
    public async Task TheRecordingsWaitingToBeDetected()
    {
        Recording older = await RecordedAsync();
        Recording newer = await RecordedAsync();
        Recording unmatched = await RecordedAsync();
        Recording detected = await RecordedAsync();
        Recording passed = await RecordedAsync();
        RecordingId thrownAway = RecordingId.New();
        await ReadAsync(older.Id, Noon.AddDays(-3), matched: true);
        await ReadAsync(newer.Id, Noon.AddDays(-2), matched: true);
        await ReadAsync(unmatched.Id, Noon.AddDays(-1), matched: false);
        await ReadAsync(detected.Id, Noon.AddDays(-1), matched: true);
        await ReadAsync(passed.Id, Noon.AddDays(-1).AddMinutes(30), matched: true);
        await ReadAsync(thrownAway, Noon, matched: true);
        await KeepAsync(Detection(detected.Id, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, []), at: Noon.AddDays(1)));

        Assert.Equal(newer.Id, (await NextAsync(1, [passed.Id]))?.RecordingId);
        Assert.Equal(older.Id, (await NextAsync(1, [passed.Id, newer.Id]))?.RecordingId);
        Assert.Null(await NextAsync(1, [passed.Id, newer.Id, older.Id]));
        Assert.Equal(detected.Id, (await NextAsync(2, [passed.Id, newer.Id, older.Id]))?.RecordingId);

        await using NpgsqlConnection connection = await database.OpenAsync();
        await ExecuteAsync(
            connection,
            $"UPDATE segment_extraction SET updated_at = timestamptz '2026-10-20T12:00:00Z', matched_at = timestamptz '2026-10-20T12:00:00Z' WHERE recording_id = '{detected.Id.Value}'");

        Assert.Equal(detected.Id, (await NextAsync(1, [passed.Id, newer.Id, older.Id]))?.RecordingId);
    }

    private static string Version(int number, string state, string adoptedAt)
        => $$"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at)
            VALUES ({{number}}, {{state}}, false, 2, '{}', '{}', '{}', '{}', '{}', now(), {{adoptedAt}})
            """;

    private static SegmentSpan Span(double starts, double ends, SegmentKind kind, double probability)
        => new(TimeSpan.FromSeconds(starts), TimeSpan.FromSeconds(ends), kind, probability);

    private static RecordingDetection Detection(RecordingId recording, SegmentDetection detection, DateTime? at = null)
        => RecordingDetection.Of(recording, 1, detection, 2, (at ?? Noon).AddMinutes(-1), at ?? Noon);

    private static async Task ExecuteAsync(NpgsqlConnection connection, string statement)
    {
        await using var command = new NpgsqlCommand(statement, connection);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> ScalarAsync(NpgsqlConnection connection, string query)
    {
        await using var command = new NpgsqlCommand(query, connection);

        return Convert.ToString(await command.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string?> ScalarAsync(string query)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        return await ScalarAsync(connection, query);
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<SegmentModelVersion> ActiveAsync()
    {
        await using CarinaDbContext context = Context();

        return await new SegmentModelVersionRepository(context).ActiveAsync(CancellationToken.None);
    }

    private async Task KeepAsync(RecordingDetection detection)
    {
        await using CarinaDbContext context = Context();

        await new RecordingDetectionRepository(context).KeepAsync(detection, CancellationToken.None);
    }

    private async Task<RecordingDetection?> FindAsync(RecordingId recording, int version)
    {
        await using CarinaDbContext context = Context();

        return await new RecordingDetectionRepository(context).FindAsync(recording, version, CancellationToken.None);
    }

    private async Task<LearningExtraction?> NextAsync(int version, IReadOnlyCollection<RecordingId> passedOver)
    {
        await using CarinaDbContext context = Context();

        return await new DetectionWorklist(context).NextAsync(version, passedOver, CancellationToken.None);
    }

    private async Task ReadAsync(RecordingId recording, DateTime startedAt, bool matched)
    {
        ProgrammeCopy programme = new(new NetworkId(1), new ServiceId(1), startedAt, startedAt.AddMinutes(30), startedAt, "架空の物語 #1", [], [], 1, AudioMode.Stereo, "架空の物語");
        LearningExtraction record = LearningExtraction.Waiting(recording, programme, startedAt);

        record.Read(ExtractionVersion.Current, startedAt);
        record.Reached(HalfAnHour, startedAt.AddMinutes(30));
        record.Finish(startedAt.AddMinutes(31));

        if (matched)
        {
            record.Matched(startedAt.AddMinutes(32));
        }

        await using CarinaDbContext context = Context();
        await new LearningExtractionRepository(context).AddAsync(record, CancellationToken.None);
    }

    private async Task<Recording> RecordedAsync()
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_001), new ServiceId(60_001), new EventId(nextEvent++), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot("架空の番組", "架空のあらすじ", string.Empty, [new ProgrammeGenre(7, 0)], Noon, AudioMode.Stereo, ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon);

        recording.Wrote(HalfAnHour);
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 3_400_000, Noon.AddMinutes(30));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
