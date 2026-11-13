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
public sealed class SeekEventSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc);

    private int nextEvent = 8_700;

    [Fact(DisplayName = "BR-CD-002: the seeks of a recording outlive the recording")]
    public async Task TheSeeksOutliveTheRecording()
    {
        Recording recording = await RecordedAsync();
        Assert.True(await TakeAsync(Seek(recording.Id, Noon)));

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await using var erasing = new NpgsqlCommand("DELETE FROM recording WHERE id = @recording", connection);
            erasing.Parameters.AddWithValue("recording", recording.Id.Value);
            Assert.Equal(1, await erasing.ExecuteNonQueryAsync());
        }

        Assert.Equal("1", await ScalarAsync($"SELECT count(*) FROM segment_seek_event WHERE recording_id = '{recording.Id.Value}'"));
    }

    [Fact(DisplayName = "BR-CV-004: a seek is kept with whether it went forward or came back and how it was made by name, the moments as intervals, and nothing of who made it")]
    public async Task ASeekIsKept()
    {
        RecordingId recording = RecordingId.New();
        RecordingId returned = RecordingId.New();

        Assert.True(await TakeAsync(Seek(recording, Noon)));
        Assert.True(await TakeAsync(SeekEvent.Of(returned, new ViewerSeek(Noon, SeekAction.Returned, SeekMeans.Key, TimeSpan.FromSeconds(700), TimeSpan.FromSeconds(612.5)))));

        Assert.Equal(
            "Forward Step 00:10:00 00:11:42.3",
            await ScalarAsync($"SELECT concat_ws(' ', event, means, from_at, to_at) FROM segment_seek_event WHERE recording_id = '{recording.Value}'"));
        Assert.Equal(
            "Returned Key 00:10:12.5",
            await ScalarAsync($"SELECT concat_ws(' ', event, means, to_at) FROM segment_seek_event WHERE recording_id = '{returned.Value}'"));
        Assert.Equal(
            "event from_at happened_at id means recording_id to_at",
            await ScalarAsync("SELECT string_agg(column_name, ' ' ORDER BY column_name) FROM information_schema.columns WHERE table_name = 'segment_seek_event'"));
    }

    [Fact(DisplayName = "BR-CV-004: six hundred seeks of a recording are taken within an hour and the next is not, while another recording, its skip events and the hour after are unaffected")]
    public async Task SixHundredSeeksAreTakenWithinAnHour()
    {
        RecordingId recording = RecordingId.New();
        RecordingId other = RecordingId.New();
        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await ExecuteAsync(
                connection,
                $"""
                INSERT INTO segment_seek_event (recording_id, happened_at, event, means, from_at, to_at)
                SELECT '{recording.Value}', timestamptz '2026-10-26T11:00:01Z' + make_interval(secs => n), 'Forward', 'Bar', interval '600 seconds', interval '690 seconds'
                FROM generate_series(1, 599) AS n
                """);
        }

        Assert.True(await TakeAsync(Seek(recording, Noon)));
        Assert.False(await TakeAsync(Seek(recording, Noon.AddSeconds(1))));
        Assert.True(await TakeAsync(Seek(other, Noon.AddSeconds(1))));
        Assert.True(await TakeAsync(Seek(recording, Noon.AddHours(1).AddSeconds(1))));
        Assert.Equal("601", await ScalarAsync($"SELECT count(*) FROM segment_seek_event WHERE recording_id = '{recording.Value}'"));

        await using CarinaDbContext context = Context();
        Assert.True(await new SkipEventRepository(context).TakeAsync(
            SkipEvent.Of(recording, new ViewerAction(Noon.AddSeconds(2), ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(690), null)),
            CancellationToken.None));
    }

    [Theory(DisplayName = "the seek table refuses a seek it does not know, a way of seeking it does not know, a moment before the recording's zero, and a seek going the other way from what it says")]
    [InlineData("event", "'Watched'", "ck_segment_seek_event_event")]
    [InlineData("means", "'Mouse'", "ck_segment_seek_event_means")]
    [InlineData("from_at", "interval '-1 second'", "ck_segment_seek_event_moments")]
    [InlineData("to_at", "interval '599 seconds'", "ck_segment_seek_event_way")]
    [InlineData("event", "'Returned'", "ck_segment_seek_event_way")]
    public async Task TheSeekTableRefusesWhatDoesNotHold(string column, string value, string constraint)
    {
        Dictionary<string, string> row = new(StringComparer.Ordinal)
        {
            ["recording_id"] = $"'{Guid.NewGuid()}'",
            ["happened_at"] = "now()",
            ["event"] = "'Forward'",
            ["means"] = "'Bar'",
            ["from_at"] = "interval '600 seconds'",
            ["to_at"] = "interval '690 seconds'",
        };
        row[column] = value;
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refused = await Assert.ThrowsAsync<PostgresException>(
            () => ExecuteAsync(connection, $"INSERT INTO segment_seek_event ({string.Join(", ", row.Keys)}) VALUES ({string.Join(", ", row.Values)})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refused.SqlState);
        Assert.Equal(constraint, refused.ConstraintName);
    }

    [Fact(DisplayName = "the seeks are found by recording and moment, and by moment alone")]
    public async Task TheIndexesAreTheOnesReadBy()
    {
        Assert.Equal(
            "(happened_at) (recording_id, happened_at)",
            await ScalarAsync(
                """
                SELECT string_agg(substring(indexdef FROM '\(.*\)'), ' ' ORDER BY indexdef)
                FROM pg_indexes WHERE tablename = 'segment_seek_event' AND indexname <> 'pk_segment_seek_event'
                """));
    }

    [Fact(DisplayName = "BR-CV-004: learning reads the seeks of the recordings it names, the earliest first, and nothing of other recordings")]
    public async Task LearningReadsEverySeek()
    {
        RecordingId viewed = RecordingId.New();
        await TakeAsync(Seek(viewed, Noon.AddMinutes(9)));
        await TakeAsync(SeekEvent.Of(viewed, new ViewerSeek(Noon.AddMinutes(3), SeekAction.Forward, SeekMeans.Chapter, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(120))));
        RecordingId other = RecordingId.New();
        await TakeAsync(Seek(other, Noon.AddMinutes(4)));

        await using CarinaDbContext context = Context();
        IReadOnlyList<SeekEvent> seeks = await new SeekEventRepository(context).ListAsync([viewed, RecordingId.New()], CancellationToken.None);

        Assert.Equal(
            [(Noon.AddMinutes(3), SeekMeans.Chapter), (Noon.AddMinutes(9), SeekMeans.Step)],
            seeks.Select(seek => (seek.HappenedAt, seek.Means)));
    }

    private static SeekEvent Seek(RecordingId recording, DateTime at)
        => SeekEvent.Of(recording, new ViewerSeek(at, SeekAction.Forward, SeekMeans.Step, TimeSpan.FromSeconds(600), TimeSpan.FromSeconds(702.3)));

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

    private async Task<bool> TakeAsync(SeekEvent seek)
    {
        await using CarinaDbContext context = Context();

        return await new SeekEventRepository(context).TakeAsync(seek, CancellationToken.None);
    }

    private async Task<Recording> RecordedAsync()
    {
        RecordingId id = RecordingId.New();
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(40_003), new ServiceId(60_003), new EventId(nextEvent++), Noon),
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
