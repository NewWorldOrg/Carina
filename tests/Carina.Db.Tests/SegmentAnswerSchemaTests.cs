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
public sealed class SegmentAnswerSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan HalfAnHour = TimeSpan.FromMinutes(30);

    private int nextEvent = 8_600;

    [Fact(DisplayName = "BR-CV-001: an answer is kept as it was sent and read back: its stretches by kind name and with no probability, the length it was read against, and since when it is confirmed")]
    public async Task AnAnswerIsKeptAsItWasSent()
    {
        RecordingId recording = RecordingId.New();
        RecordingAnswer given = Given(recording, confirmed: true, Noon, (0, 912.3, SegmentKind.Programme), (912.3, 1002.3, SegmentKind.Break), (1002.3, 1800, SegmentKind.Programme));

        await AnswerAsync(given);

        RecordingAnswer kept = (await FindAsync(recording))!;
        Assert.Equal(given.Segments, kept.Segments);
        Assert.Equal(
            (HalfAnHour, true, (DateTime?)Noon, Noon, 1, (DateTime?)null),
            (kept.Length, kept.Confirmed, kept.ConfirmedAt, kept.AnsweredAt, kept.Answers, kept.WithdrawnAt));
        Assert.Equal(
            "t 00:30:00",
            await ScalarAsync(
                $$"""
                SELECT concat_ws(' ',
                    segments = '[{"startsAtSec": 0, "endsAtSec": 912.3, "kind": "Programme"}, {"startsAtSec": 912.3, "endsAtSec": 1002.3, "kind": "Break"}, {"startsAtSec": 1002.3, "endsAtSec": 1800, "kind": "Programme"}]'::jsonb,
                    length)
                FROM segment_answer WHERE recording_id = '{{recording.Value}}'
                """));
    }

    [Fact(DisplayName = "BR-CV-001: an answer sent again writes over the one kept, one row a recording, one more answer counted, confirmed only when it says so, and standing again once withdrawn")]
    public async Task AnAnswerSentAgainWritesOverTheOneKept()
    {
        RecordingId recording = RecordingId.New();
        await AnswerAsync(Given(recording, confirmed: true, Noon, (0, 1800, SegmentKind.Programme)));
        Assert.True(await WithdrawAsync(recording, Noon.AddMinutes(1)));

        await AnswerAsync(Given(recording, confirmed: false, Noon.AddMinutes(2), (0, 300, SegmentKind.Programme), (300, 360, SegmentKind.Break), (360, 1800, SegmentKind.Programme)));
        await AnswerAsync(Given(recording, confirmed: false, Noon.AddMinutes(3), (0, 300, SegmentKind.Programme), (300, 390, SegmentKind.Break), (390, 1800, SegmentKind.Programme)));

        RecordingAnswer kept = (await FindAsync(recording))!;
        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM segment_answer WHERE recording_id = '{recording.Value}'"));
        Assert.Equal((3, Noon.AddMinutes(3), (DateTime?)null, true), (kept.Answers, kept.AnsweredAt, kept.WithdrawnAt, kept.Stands));
        Assert.Equal((false, (DateTime?)null), (kept.Confirmed, kept.ConfirmedAt));
        Assert.Equal(TimeSpan.FromSeconds(390), kept.Segments[1].Ends);
    }

    [Fact(DisplayName = "BR-CD-002: withdrawing an answer marks its row and keeps it; an answer already withdrawn, or none at all, is not withdrawn again")]
    public async Task WithdrawingAnAnswerKeepsItsRow()
    {
        RecordingId recording = RecordingId.New();
        RecordingAnswer given = Given(recording, confirmed: true, Noon, (0, 600, SegmentKind.Programme), (600, 660, SegmentKind.Break), (660, 1800, SegmentKind.Programme));
        await AnswerAsync(given);

        Assert.True(await WithdrawAsync(recording, Noon.AddHours(1)));
        Assert.False(await WithdrawAsync(recording, Noon.AddHours(2)));
        Assert.False(await WithdrawAsync(RecordingId.New(), Noon.AddHours(2)));

        RecordingAnswer kept = (await FindAsync(recording))!;
        Assert.Equal(((DateTime?)Noon.AddHours(1), false), (kept.WithdrawnAt, kept.Stands));
        Assert.Equal(given.Segments, kept.Segments);
        Assert.Equal((true, 1), (kept.Confirmed, kept.Answers));
    }

    [Fact(DisplayName = "BR-CD-002: the answer and the skip events of a recording outlive the recording")]
    public async Task TheAnswerAndTheSkipEventsOutliveTheRecording()
    {
        Recording recording = await RecordedAsync();
        await AnswerAsync(Given(recording.Id, confirmed: true, Noon, (0, 1800, SegmentKind.Programme)));
        Assert.True(await TakeAsync(Skip(recording.Id, Noon, 1)));

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var erasing = new NpgsqlCommand("DELETE FROM recording WHERE id = @recording", connection);
            erasing.Parameters.AddWithValue("recording", recording.Id.Value);
            Assert.Equal(1, await erasing.ExecuteNonQueryAsync());
        }

        Assert.NotNull(await FindAsync(recording.Id));
        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM segment_skip_event WHERE recording_id = '{recording.Id.Value}'"));
    }

    [Fact(DisplayName = "BR-CV-003: a skip event is kept with the kind and action by name, the moments as intervals and the version, and with nothing of who did it")]
    public async Task ASkipEventIsKept()
    {
        RecordingId recording = RecordingId.New();
        RecordingId answered = RecordingId.New();

        Assert.True(await TakeAsync(Skip(recording, Noon, 1)));
        Assert.True(await TakeAsync(SkipEvent.Of(answered, new ViewerAction(Noon, ViewerActionKind.Returned, SegmentKind.Ending, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(12.5), null))));

        Assert.Equal(
            "Skipped Break 00:15:15 00:16:42.3 1",
            await ScalarAsync($"SELECT concat_ws(' ', event, kind, from_at, to_at, model_version) FROM segment_skip_event WHERE recording_id = '{recording.Value}'"));
        Assert.Equal(
            "Returned Ending NULL",
            await ScalarAsync($"SELECT concat_ws(' ', event, kind, coalesce(model_version::text, 'NULL')) FROM segment_skip_event WHERE recording_id = '{answered.Value}'"));
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

    [Theory(DisplayName = "the answer table refuses an answer confirmed without saying since when, or unconfirmed saying so, one sent no times, and stretches that are not a list")]
    [InlineData("confirmed", "true", "ck_segment_answer_confirmed")]
    [InlineData("confirmed_at", "now()", "ck_segment_answer_confirmed")]
    [InlineData("answers", "0", "ck_segment_answer_answers")]
    [InlineData("segments", "'{}'", "ck_segment_answer_segments")]
    [InlineData("segments", "'[]'", "ck_segment_answer_segments")]
    public async Task TheAnswerTableRefusesWhatDoesNotHold(string column, string value, string constraint)
    {
        Dictionary<string, string> row = new(StringComparer.Ordinal)
        {
            ["recording_id"] = $"'{Guid.NewGuid()}'",
            ["segments"] = "'[{\"kind\": \"Programme\", \"startsAtSec\": 0, \"endsAtSec\": 1800}]'",
            ["length"] = "interval '30 minutes'",
            ["confirmed"] = "false",
            ["confirmed_at"] = "NULL",
            ["answered_at"] = "now()",
            ["answers"] = "1",
        };
        row[column] = value;
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connection, $"INSERT INTO segment_answer ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
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

    [Fact(DisplayName = "the confirmed answers that stand are found by an index of their own, and the events by recording and moment and by moment alone")]
    public async Task TheIndexesAreTheOnesReadBy()
    {
        Assert.Equal(
            "CREATE INDEX ix_segment_answer_confirmed ON public.segment_answer USING btree (confirmed_at) WHERE (confirmed AND (withdrawn_at IS NULL))",
            await ScalarAsync("SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_segment_answer_confirmed'"));
        Assert.Equal(
            "(happened_at) (model_version) (recording_id, happened_at)",
            await ScalarAsync(
                """
                SELECT string_agg(substring(indexdef FROM '\(.*\)'), ' ' ORDER BY indexdef)
                FROM pg_indexes WHERE tablename = 'segment_skip_event' AND indexname <> 'pk_segment_skip_event'
                """));
    }

    [Fact(DisplayName = "BR-CD-006: learning reads every answer kept, withdrawn or not, and every skip event, the earliest first")]
    public async Task LearningReadsEveryAnswerAndEveryEvent()
    {
        RecordingId standing = RecordingId.New();
        RecordingId withdrawn = RecordingId.New();
        await AnswerAsync(Given(standing, confirmed: true, Noon, (0, 600, SegmentKind.Programme), (600, 660, SegmentKind.Break), (660, 1800, SegmentKind.Programme)));
        await AnswerAsync(Given(withdrawn, confirmed: false, Noon, (0, 1800, SegmentKind.Programme)));
        await WithdrawAsync(withdrawn, Noon.AddHours(1));
        await TakeAsync(Skip(standing, Noon.AddMinutes(9), null));
        await TakeAsync(Skip(standing, Noon.AddMinutes(3), SegmentModelVersion.FirstNumber));

        await using CarinaDbContext context = Context();
        IReadOnlyList<RecordingAnswer> answers = await new SegmentAnswerRepository(context).ListAsync(CancellationToken.None);
        IReadOnlyList<SkipEvent> events = await new SkipEventRepository(context).ListAsync(CancellationToken.None);

        Assert.Equal(
            [(standing, true), (withdrawn, false)],
            answers.Where(answer => answer.RecordingId.Equals(standing) || answer.RecordingId.Equals(withdrawn)).OrderBy(answer => answer.Confirmed ? 0 : 1).Select(answer => (answer.RecordingId, answer.Stands)));
        Assert.Equal(
            [(Noon.AddMinutes(3), (int?)SegmentModelVersion.FirstNumber), (Noon.AddMinutes(9), null)],
            events.Where(skip => skip.RecordingId.Equals(standing)).Select(skip => (skip.HappenedAt, skip.ModelVersion)));
        Assert.Equal(events.OrderBy(skip => skip.HappenedAt).ThenBy(skip => skip.Id).Select(skip => skip.Id), events.Select(skip => skip.Id));
    }

    private static string InsertSkip(string version)
        => $"""
            INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at, model_version)
            VALUES ('{Guid.NewGuid()}', now(), 'Skipped', 'Break', interval '915 seconds', interval '1002 seconds', {version})
            """;

    private static RecordingAnswer Given(RecordingId recording, bool confirmed, DateTime at, params (double Starts, double Ends, SegmentKind Kind)[] stretches)
        => RecordingAnswer.Given(
            recording,
            AnsweredStretches.Read([.. stretches.Select(stretch => new SentStretch(stretch.Starts, stretch.Ends, stretch.Kind))], HalfAnHour).Stretches!,
            confirmed,
            at);

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

    private async Task AnswerAsync(RecordingAnswer answer)
    {
        await using CarinaDbContext context = Context();

        await new SegmentAnswerRepository(context).AnswerAsync(answer, CancellationToken.None);
    }

    private async Task<RecordingAnswer?> FindAsync(RecordingId recording)
    {
        await using CarinaDbContext context = Context();

        return await new SegmentAnswerRepository(context).FindAsync(recording, CancellationToken.None);
    }

    private async Task<bool> WithdrawAsync(RecordingId recording, DateTime at)
    {
        await using CarinaDbContext context = Context();

        return await new SegmentAnswerRepository(context).WithdrawAsync(recording, at, CancellationToken.None);
    }

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

        recording.Wrote(HalfAnHour);
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 3_400_000, Noon.AddMinutes(30));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return recording;
    }
}
