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

    [Fact(DisplayName = "BR-CS-003: from the start the latest version of four kinds made by hand, taking adverts in pods for breaks the more, is in use in place of the first, with no figures of an evaluation; the first is kept as used before, and the versions of four kinds made by hand before the latest, the later without the catalog's weights as the earlier now is, the next hearing silence from -55 dBFS, the one after telling the opening and the ending in any series and the next taking songs whose loudness moves for them, wait beside it as candidates")]
    public async Task TheLatestVersionMadeByHandIsInUseFromTheStart()
    {
        await using CarinaDbContext context = Context();
        IReadOnlyList<SegmentModelVersion> kept = await new SegmentModelVersionRepository(context).ListAsync(CancellationToken.None);
        SegmentModelVersion first = kept.Single(version => version.Number == SegmentModelVersion.FirstNumber);
        SegmentModelVersion four = kept.Single(version => version.Number == SegmentModelVersion.FirstNumber + 1);
        SegmentModelVersion later = kept.Single(version => version.Number == SegmentModelVersion.FirstNumber + 2);
        SegmentModelVersion belowPauses = kept.Single(version => version.Number == SegmentModelVersion.FirstNumber + 3);
        SegmentModelVersion anySeries = kept.Single(version => version.Number == SegmentModelVersion.FirstNumber + 4);
        SegmentModelVersion dynamicSongs = kept.Single(version => version.Number == SegmentModelVersion.FirstNumber + 5);
        SegmentModelVersion active = await ActiveAsync();

        Assert.Equal(SegmentModelVersion.FirstNumber + 6, active.Number);
        Assert.False(active.ByHand);
        Assert.Equal(4, active.Kinds);
        Assert.Equal(SegmentModel.FourKindsInPods, active.Model);
        Assert.NotNull(active.AdoptedAt);
        Assert.Null(active.RetiredAt);
        Assert.Null(active.Evaluation);
        Assert.Equal((SegmentModelVersionState.Retired, 2, SegmentModel.First), (first.State, first.Kinds, first.Model));
        Assert.Equal(first.TrainedAt, first.AdoptedAt);
        Assert.NotNull(first.RetiredAt);
        Assert.Equal(SegmentModelVersionState.Candidate, four.State);
        Assert.Equal(4, four.Kinds);
        Assert.Equal(SegmentModel.FirstOfFourKinds, four.Model);
        Assert.Null(four.Evaluation);
        Assert.Equal((SegmentModelVersionState.Candidate, SegmentModel.FirstOfFourKinds, (SegmentEvaluation?)null), (later.State, later.Model, later.Evaluation));
        Assert.Equal((SegmentModelVersionState.Candidate, SegmentModel.FourKindsBelowPauses, (SegmentEvaluation?)null), (belowPauses.State, belowPauses.Model, belowPauses.Evaluation));
        Assert.Equal((SegmentModelVersionState.Candidate, SegmentModel.FourKindsInAnySeries, (SegmentEvaluation?)null), (anySeries.State, anySeries.Model, anySeries.Evaluation));
        Assert.Equal((SegmentModelVersionState.Candidate, SegmentModel.FourKindsWithDynamicSongs, (SegmentEvaluation?)null), (dynamicSongs.State, dynamicSongs.Model, dynamicSongs.Evaluation));
        Assert.Equal(
            "NULL NULL NULL NULL",
            await ScalarAsync("SELECT concat_ws(' ', coalesce(metrics::text, 'NULL'), coalesce(stations::text, 'NULL'), coalesce(evidence::text, 'NULL'), coalesce(rollback::text, 'NULL')) FROM segment_model_version WHERE number = 1"));
    }

    [Fact(DisplayName = "BR-CS-003: exactly one version is in use: a second one in use is refused by the unique index, while any number of candidates and versions used before are kept beside it")]
    public async Task ExactlyOneVersionIsInUse()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using NpgsqlTransaction transaction = await connection.BeginTransactionAsync();

        await ExecuteAsync(connection, Version(21, "'Candidate'", "NULL"));
        await ExecuteAsync(connection, Version(22, "'Candidate'", "NULL"));
        await ExecuteAsync(connection, Version(23, "'Retired'", "now()", retiredAt: "now()"));
        await ExecuteAsync(connection, Version(24, "'Retired'", "now()", retiredAt: "now()"));
        await ExecuteAsync(connection, "SAVEPOINT before_second");
        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection, Version(25, "'Active'", "now()")));
        await ExecuteAsync(connection, "ROLLBACK TO SAVEPOINT before_second");

        Assert.Equal(PostgresErrorCodes.UniqueViolation, refused.SqlState);
        Assert.Equal("ux_segment_model_version_active", refused.ConstraintName);
        Assert.Equal("1", await ScalarAsync(connection, "SELECT count(*) FROM segment_model_version WHERE state = 'Active'"));
        Assert.Equal(
            "Candidate Candidate Retired Retired",
            await ScalarAsync(connection, "SELECT string_agg(state, ' ' ORDER BY number) FROM segment_model_version WHERE number BETWEEN 21 AND 25"));

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
        await ExecuteAsync(connection, Version(32, "'Candidate'", "NULL"));
        await ExecuteAsync(
            connection,
            $"""
            INSERT INTO segment_detection (recording_id, model_version, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at)
            SELECT recording_id, 32, state, segments, cm_share, snaps, extraction_version, catalog_until, detected_at FROM segment_detection WHERE recording_id = '{recording.Value}'
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

    [Fact(DisplayName = "the earlier recordings on a station are those read on the same station that started strictly before the one judged, the most recently started first, never the one judged")]
    public async Task TheEarlierRecordingsOnAStation()
    {
        DateTime judgedAt = Noon.AddDays(40);
        RecordingId judged = RecordingId.New();
        RecordingId lastWeek = RecordingId.New();
        RecordingId twoWeeksAgo = RecordingId.New();
        RecordingId later = RecordingId.New();
        RecordingId sameTime = RecordingId.New();
        RecordingId elsewhere = RecordingId.New();
        RecordingId unread = RecordingId.New();
        await ReadAsync(judged, judgedAt, matched: true, station: 7);
        await ReadAsync(lastWeek, judgedAt.AddDays(-7), matched: false, station: 7);
        await ReadAsync(twoWeeksAgo, judgedAt.AddDays(-14), matched: true, station: 7);
        await ReadAsync(later, judgedAt.AddDays(1), matched: true, station: 7);
        await ReadAsync(sameTime, judgedAt, matched: true, station: 7);
        await ReadAsync(elsewhere, judgedAt.AddDays(-1), matched: true, station: 8);
        await AddAsync(LearningExtraction.Waiting(unread, Copy(judgedAt.AddDays(-3), 7), judgedAt.AddDays(-3)));
        SeriesMember member = Copy(judgedAt, 7).MemberAs(judged);

        Assert.Equal([lastWeek, twoWeeksAgo], (await EarlierAsync(member, 200)).Select(record => record.RecordingId));
        Assert.Equal([lastWeek], (await EarlierAsync(member, 1)).Select(record => record.RecordingId));
    }

    [Fact(DisplayName = "the records named are read when read to their end or as far as they could be, the earliest started first")]
    public async Task TheRecordsNamedAreReadWhenRead()
    {
        RecordingId later = RecordingId.New();
        RecordingId earlier = RecordingId.New();
        RecordingId unread = RecordingId.New();
        RecordingId other = RecordingId.New();
        await ReadAsync(later, Noon.AddDays(50), matched: true, station: 9);
        await ReadAsync(earlier, Noon.AddDays(49), matched: false, station: 9);
        await ReadAsync(other, Noon.AddDays(48), matched: true, station: 9);
        await AddAsync(LearningExtraction.Waiting(unread, Copy(Noon.AddDays(47), 9), Noon.AddDays(47)));

        await using CarinaDbContext context = Context();
        IReadOnlyList<LearningExtraction> read = await new DetectionWorklist(context).RecordsAsync([later, earlier, unread, RecordingId.New()], CancellationToken.None);

        Assert.Equal([earlier, later], read.Select(record => record.RecordingId));
    }

    [Fact(DisplayName = "the learning data of the kinds asked for is read for the recording alone, by chunk, leaving out a chunk that cannot be read")]
    public async Task TheLearningDataOfTheKindsAskedForIsRead()
    {
        RecordingId recording = RecordingId.New();
        RecordingId other = RecordingId.New();
        await KeepAsync(Block(recording, LearningDataKind.Loudness, 1));
        await KeepAsync(Block(recording, LearningDataKind.Loudness, 0));
        await KeepAsync(Block(recording, LearningDataKind.SoundFingerprints, 0));
        await KeepAsync(Block(other, LearningDataKind.Loudness, 0));
        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(
                connection,
                $"INSERT INTO segment_learning_data (recording_id, kind, chunk, bytes, version_number, version_origin, written_at) VALUES ('{recording.Value}', {(short)LearningDataKind.Loudness}, 2, '\\x01'::bytea, 2, 'RecordingFile', now())");
        }

        await using CarinaDbContext context = Context();
        IReadOnlyList<LearningDataPart> parts = await new DetectionWorklist(context).PartsAsync(recording, [LearningDataKind.Loudness], CancellationToken.None);

        Assert.Equal([(LearningDataKind.Loudness, 0), (LearningDataKind.Loudness, 1)], parts.Select(part => (part.Kind, part.Index)));
    }

    [Fact(DisplayName = "the detections listed are those of the recordings named made by the version named")]
    public async Task TheDetectionsListedAreThoseOfTheVersionNamed()
    {
        RecordingId first = RecordingId.New();
        RecordingId second = RecordingId.New();
        RecordingId unnamed = RecordingId.New();
        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(connection, Version(31, "'Candidate'", "NULL"));
        }

        await KeepAsync(Detection(first, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, [])));
        await KeepAsync(Detection(second, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, [])));
        await KeepAsync(Detection(unnamed, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, [])));
        await KeepAsync(RecordingDetection.Of(second, 31, SegmentDetection.Judged([Span(0, 1800, SegmentKind.Programme, 0.97)], HalfAnHour, []), 2, Noon, Noon));

        await using CarinaDbContext context = Context();
        RecordingDetectionRepository detections = new(context);
        IReadOnlyList<RecordingDetection> byTheFirst = await detections.ListAsync([first, second, RecordingId.New()], 1, CancellationToken.None);
        IReadOnlyList<RecordingDetection> byTheOther = await detections.ListAsync([first, second], 31, CancellationToken.None);

        Assert.Equal([first, second], byTheFirst.Select(detection => detection.RecordingId).OrderBy(id => id.Equals(second)));
        Assert.All(byTheFirst, detection => Assert.Equal(1, detection.ModelVersion));
        Assert.Equal((second, 31), (Assert.Single(byTheOther).RecordingId, byTheOther[0].ModelVersion));
    }

    private static string Version(int number, string state, string adoptedAt, string retiredAt = "NULL")
        => $$"""
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at, retired_at)
            VALUES ({{number}}, {{state}}, false, 2, '{}', '{}', '{}', '{}', '{}', now(), {{adoptedAt}}, {{retiredAt}})
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

    private static ProgrammeCopy Copy(DateTime startedAt, int station = 1)
        => new(new NetworkId(station), new ServiceId(station), startedAt, startedAt.AddMinutes(30), startedAt, "架空の物語 #1", [], [], 1, AudioMode.Stereo, "架空の物語");

    private static LearningDataBlock Block(RecordingId recording, LearningDataKind kind, int chunk)
    {
        LearningDataChunk sample = LearningDataChunk.Of(
            chunk,
            FrameClock.Of(30000, 1001, LearningData.ChunkStarts(chunk)),
            [.. Enumerable.Range(0, 64).Select(reading => (uint)reading * 2_654_435_761u)],
            [.. Enumerable.Range(0, 64).Select(reading => (byte)(reading % 200))],
            [],
            [],
            []);

        return LearningDataBlock.Of(recording, LearningDataPart.Of(sample, kind), ExtractionVersion.Current, Noon);
    }

    private async Task KeepAsync(LearningDataBlock block)
    {
        await using CarinaDbContext context = Context();

        await new LearningDataRepository(context).KeepAsync(block, CancellationToken.None);
    }

    private async Task AddAsync(LearningExtraction record)
    {
        await using CarinaDbContext context = Context();

        await new LearningExtractionRepository(context).AddAsync(record, CancellationToken.None);
    }

    private async Task<IReadOnlyList<LearningExtraction>> EarlierAsync(SeriesMember judged, int most)
    {
        await using CarinaDbContext context = Context();

        return await new DetectionWorklist(context).EarlierOnTheStationAsync(judged, most, CancellationToken.None);
    }

    private async Task ReadAsync(RecordingId recording, DateTime startedAt, bool matched, int station = 1)
    {
        LearningExtraction record = LearningExtraction.Waiting(recording, Copy(startedAt, station), startedAt);

        record.Read(ExtractionVersion.Current, startedAt);
        record.Reached(HalfAnHour, startedAt.AddMinutes(30));
        record.Finish(startedAt.AddMinutes(31));

        if (matched)
        {
            record.Matched(startedAt.AddMinutes(32));
        }

        await AddAsync(record);
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
