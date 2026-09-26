using Carina.Domain.Auth;
using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Viewing;
using Carina.Infrastructure.Persistence;
using Carina.Infrastructure.Persistence.Repositories;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class PlaybackPositionSchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const int Network = 39_301;

    private static readonly DateTime Noon = new(2026, 9, 16, 12, 0, 0, DateTimeKind.Utc);

    [Fact(DisplayName = "one viewer keeps one place in one recording, however often a player sends it")]
    public async Task OneViewerKeepsOnePlaceInOneRecording()
    {
        RecordingId recording = await RecordedAsync(50_001);

        Assert.Equal(PlaybackPositionKeep.Kept, await KeepAsync(recording, "someone", 60));
        Assert.Equal(PlaybackPositionKeep.Kept, await KeepAsync(recording, "someone", 90));

        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(1, await KeptForAsync(connection, recording));
        Assert.Equal(TimeSpan.FromSeconds(90), (await FoundAsync(recording, "someone"))!.Position);
    }

    [Fact(DisplayName = "two viewers keep their own place in the same recording")]
    public async Task TwoViewersKeepTheirOwnPlaceInTheSameRecording()
    {
        RecordingId recording = await RecordedAsync(50_002);

        await KeepAsync(recording, "someone", 60);
        await KeepAsync(recording, "someone else", 900);

        Assert.Equal(TimeSpan.FromSeconds(60), (await FoundAsync(recording, "someone"))!.Position);
        Assert.Equal(TimeSpan.FromSeconds(900), (await FoundAsync(recording, "someone else"))!.Position);
    }

    [Fact(DisplayName = "a place before the beginning of a recording is refused by the table itself")]
    public async Task APlaceBeforeTheBeginningIsRefused()
    {
        RecordingId recording = await RecordedAsync(50_003);

        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(connection, recording, "someone", -1));

        Assert.Equal("ck_playback_position_position", refusal.ConstraintName);
    }

    [Fact(DisplayName = "the same viewer and recording are one row, whoever writes it")]
    public async Task TheSameViewerAndRecordingAreOneRow()
    {
        RecordingId recording = await RecordedAsync(50_004);

        await using NpgsqlConnection connection = await database.OpenAsync();
        await InsertAsync(connection, recording, "someone", 60_000);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => InsertAsync(connection, recording, "someone", 90_000));

        Assert.Equal("pk_playback_position", refusal.ConstraintName);
    }

    [Fact(DisplayName = "a place in a recording the ledger does not hold is not kept for nothing")]
    public async Task APlaceInARecordingTheLedgerDoesNotHoldIsNotKept()
    {
        var nowhere = new RecordingId(Guid.NewGuid());

        Assert.Equal(PlaybackPositionKeep.NoSuchRecording, await KeepAsync(nowhere, "someone", 60));

        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(0, await KeptForAsync(connection, nowhere));
    }

    [Fact(DisplayName = "forgetting a recording takes every viewer's place in it and leaves the others alone")]
    public async Task ForgettingARecordingTakesEveryViewersPlaceInIt()
    {
        RecordingId recording = await RecordedAsync(50_005);
        RecordingId left = await RecordedAsync(50_006);

        await KeepAsync(recording, "someone", 60);
        await KeepAsync(recording, "someone else", 90);
        await KeepAsync(left, "someone", 30);

        await using (CarinaDbContext context = Context())
        {
            await new PlaybackPositionRepository(context).ForgetAsync(recording, CancellationToken.None);
        }

        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(0, await KeptForAsync(connection, recording));
        Assert.Equal(1, await KeptForAsync(connection, left));
    }

    private CarinaDbContext Context() => CarinaDbContextFactory.Create(database.ConnectionString);

    private async Task<PlaybackPositionKeep> KeepAsync(RecordingId recording, string viewer, double seconds)
    {
        await using CarinaDbContext context = Context();

        return await new PlaybackPositionRepository(context).KeepAsync(
            PlaybackPosition.Reached(recording, new Subject(viewer), TimeSpan.FromSeconds(seconds), Noon),
            CancellationToken.None);
    }

    private async Task<PlaybackPosition?> FoundAsync(RecordingId recording, string viewer)
    {
        await using CarinaDbContext context = Context();

        return await new PlaybackPositionRepository(context).FindAsync(
            recording,
            new Subject(viewer),
            CancellationToken.None);
    }

    private async Task<RecordingId> RecordedAsync(int eventId)
    {
        var id = new RecordingId(Guid.NewGuid());
        Recording recording = Recording.Begin(
            id,
            null,
            new ProgrammeRef(new NetworkId(Network), new ServiceId(1024), new EventId(eventId), Noon),
            new OutputRoot("bulk"),
            RecordingFileName.For(id, ".m2ts"),
            Noon,
            Noon.AddHours(1),
            new ProgrammeSnapshot(
                "A programme",
                "What it is about",
                string.Empty,
                [],
                Noon,
                AudioMode.Undetermined,
                ProgrammeSnapshot.SoundsUnannounced),
            null,
            BroadcastGroupRole.Standalone,
            Noon,
            new TunerDeviceId("pt3-0"));

        recording.Wrote(TimeSpan.FromMinutes(30));
        recording.Abort(Noon.AddMinutes(30));
        recording.Settle(RecordingOutcome.Complete, 3_400_000, Noon.AddMinutes(30));

        await using CarinaDbContext context = Context();
        context.Add(recording);
        await context.SaveChangesAsync();

        return id;
    }

    private static async Task InsertAsync(
        NpgsqlConnection connection,
        RecordingId recording,
        string viewer,
        long positionMs)
    {
        await using var keeping = new NpgsqlCommand(
            """
            INSERT INTO playback_position (recording_id, subject, position_ms, updated_at)
            VALUES (@recording, @viewer, @position, timestamptz '2026-09-16 13:00:00+00')
            """,
            connection);
        keeping.Parameters.AddWithValue("recording", recording.Value);
        keeping.Parameters.AddWithValue("viewer", viewer);
        keeping.Parameters.AddWithValue("position", positionMs);

        await keeping.ExecuteNonQueryAsync();
    }

    private static async Task<long> KeptForAsync(NpgsqlConnection connection, RecordingId recording)
    {
        await using var counting = new NpgsqlCommand(
            "SELECT count(*) FROM playback_position WHERE recording_id = @recording",
            connection);
        counting.Parameters.AddWithValue("recording", recording.Value);

        return Convert.ToInt64(await counting.ExecuteScalarAsync());
    }
}
