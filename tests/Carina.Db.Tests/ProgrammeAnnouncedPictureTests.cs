using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ProgrammeAnnouncedPictureTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-09-13 20:00:00+00'";

    private const string Ends = "timestamptz '2026-09-13 21:00:00+00'";

    private const string Now = "timestamptz '2026-09-13 12:00:00+00'";

    private static readonly CancellationToken Cancel = CancellationToken.None;

    [Fact]
    public async Task ThePictureAProgrammeAnnouncedIsWrittenDownAndReadsBackTheSame()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Programme(connection, 90_001, "Interlaced1080", "SixteenByNine");

        Assert.Equal("Interlaced1080", await Scalar(connection, 90_001, "video"));
        Assert.Equal("SixteenByNine", await Scalar(connection, 90_001, "aspect"));
    }

    [Fact]
    public async Task AProgrammeNobodyAnnouncedAPictureForStandsAtUndetermined()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        await Programme(connection, 90_002, "Undetermined", "Undetermined");

        Assert.Equal("Undetermined", await Scalar(connection, 90_002, "video"));
        Assert.Equal("Undetermined", await Scalar(connection, 90_002, "aspect"));
    }

    [Fact]
    public async Task APictureTheVocabularyDoesNotNameIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Programme(connection, 90_003, "Interlaced1440", "SixteenByNine"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_programme_video", refusal.ConstraintName);
    }

    [Fact]
    public async Task AShapeTheVocabularyDoesNotNameIsRefused()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Programme(connection, 90_004, "Interlaced1080", "TwoByOne"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_programme_aspect", refusal.ConstraintName);
    }

    private static async Task Programme(NpgsqlConnection connection, int networkId, string video, string aspect)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id, start_at, end_at,
                name, summary, is_shadow, genres, items, related, has_subtitles, audio, sounds,
                video, aspect, source, updated_at)
            VALUES (
                {networkId}, 1024, 4001, 32736, {Airs}, {Ends},
                'A programme', 'What it is about', false, '[]'::jsonb, '[]'::jsonb, '[]'::jsonb,
                false, 'Undetermined', 0, @video, @aspect, 'ScheduleBasic', {Now})
            """;
        command.Parameters.AddWithValue("video", video);
        command.Parameters.AddWithValue("aspect", aspect);

        await command.ExecuteNonQueryAsync(Cancel);
    }

    private static async Task<string?> Scalar(NpgsqlConnection connection, int networkId, string column)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"SELECT {column} FROM programme WHERE network_id = {networkId}";

        return await command.ExecuteScalarAsync(Cancel) as string;
    }
}
