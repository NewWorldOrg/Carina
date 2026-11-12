using Carina.Domain.Recordings;
using Carina.Domain.Segments;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

using NpgsqlTypes;

namespace Carina.Db.Tests;

/// <summary>
/// The statements that keep a skip event or a seek only while its recording has taken fewer than the most an hour,
/// held to the event's own answer for the same rows: which kept events count with the new one, on the instant an
/// hour before it and a microsecond after, with one short of the most and with the most.
/// </summary>
[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ViewerActionHourArmsTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private static readonly DateTime Noon = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TimeSpan Microsecond = TimeSpan.FromTicks(10);

    public static TheoryData<string, int, double[]> Arms
        => new()
        {
            { "one short of the most, and one an hour before", SkipEvent.MostAnHour - 1, [-3600] },
            { "one short of the most, and one a microsecond after an hour before", SkipEvent.MostAnHour - 1, [-3600 + 0.000001] },
            { "one short of the most, and one later than the new one", SkipEvent.MostAnHour - 1, [300] },
            { "two short of the most, and one at the same instant", SkipEvent.MostAnHour - 2, [0] },
            { "the most, all an hour before or earlier", 0, [.. Enumerable.Repeat(-3600.0, SkipEvent.MostAnHour)] },
            { "none", 0, [] },
        };

    [Theory(DisplayName = "BR-CV-003: the store keeps a skip event exactly when the event says it is taken beside those its recording kept")]
    [MemberData(nameof(Arms))]
    public async Task TheStoreKeepsASkipWhenTheEventSaysSo(string arm, int withinTheHalfHour, double[] secondsFromTheNew)
    {
        RecordingId recording = RecordingId.New();
        DateTime[] laid = Laid(withinTheHalfHour, secondsFromTheNew);
        await LayAsync("INSERT INTO segment_skip_event (recording_id, happened_at, event, kind, from_at, to_at) SELECT @recording, at, 'Skipped', 'Break', interval '915 seconds', interval '1002 seconds' FROM unnest(@laid) AS at", recording, laid);
        SkipEvent skip = SkipEvent.Of(recording, new ViewerAction(Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002), null));

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        Assert.True(skip.IsTakenBeside(laid) == await new SkipEventRepository(context).TakeAsync(skip, CancellationToken.None), arm);
    }

    [Theory(DisplayName = "BR-CV-004: the store keeps a seek exactly when the seek says it is taken beside those its recording kept")]
    [MemberData(nameof(Arms))]
    public async Task TheStoreKeepsASeekWhenTheSeekSaysSo(string arm, int withinTheHalfHour, double[] secondsFromTheNew)
    {
        RecordingId recording = RecordingId.New();
        DateTime[] laid = Laid(withinTheHalfHour, secondsFromTheNew);
        await LayAsync("INSERT INTO segment_seek_event (recording_id, happened_at, event, means, from_at, to_at) SELECT @recording, at, 'Forward', 'Bar', interval '915 seconds', interval '1002 seconds' FROM unnest(@laid) AS at", recording, laid);
        SeekEvent seek = SeekEvent.Of(recording, new ViewerSeek(Noon, SeekAction.Forward, SeekMeans.Bar, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002)));

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);

        Assert.True(seek.IsTakenBeside(laid) == await new SeekEventRepository(context).TakeAsync(seek, CancellationToken.None), arm);
    }

    [Fact(DisplayName = "BR-CV-003: the arms reach both answers, so that agreeing is not agreeing on one")]
    public void TheArmsReachBothAnswers()
    {
        SkipEvent skip = SkipEvent.Of(RecordingId.New(), new ViewerAction(Noon, ViewerActionKind.Skipped, SegmentKind.Break, TimeSpan.FromSeconds(915), TimeSpan.FromSeconds(1002), null));
        bool[] answers = [.. Arms.Select(arm => skip.IsTakenBeside(Laid((int)arm[1], (double[])arm[2])))];

        Assert.Equal([true, false, false, true, true, true], answers);
    }

    private static DateTime[] Laid(int withinTheHalfHour, double[] secondsFromTheNew)
        =>
        [
            .. Enumerable.Range(1, withinTheHalfHour).Select(index => Noon.AddMinutes(-30).AddMilliseconds(index)),
            .. secondsFromTheNew.Select(seconds => Noon + TimeSpan.FromTicks((long)Math.Round(seconds * TimeSpan.TicksPerSecond / Microsecond.Ticks) * Microsecond.Ticks)),
        ];

    private async Task LayAsync(string statement, RecordingId recording, DateTime[] laid)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await using var command = new NpgsqlCommand(statement, connection);
        command.Parameters.AddWithValue("recording", recording.Value);
        command.Parameters.Add(new NpgsqlParameter("laid", NpgsqlDbType.Array | NpgsqlDbType.TimestampTz) { Value = laid });

        await command.ExecuteNonQueryAsync();
    }
}
