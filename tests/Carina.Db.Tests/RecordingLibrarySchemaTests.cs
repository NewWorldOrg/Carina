using Carina.Domain.Programmes;
using Carina.Infrastructure.Persistence.Configurations;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingLibrarySchemaTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    private const string WrittenToAGeneratedColumn = "428C9";

    [Theory]
    [InlineData(43011, "ＮＥＥＤＹ", "ｷﾞｮｳｻﾞ", "ﾀﾅｶ", "needy ギョウザ タナカ")]
    [InlineData(43012, "ニュース①", "", "", "ニュース1  ")]
    [InlineData(43013, "Ｇｕｉｄｅ", "", "", "guide  ")]
    public async Task WhatTheGuideFoldsForItsOwnSearchIsWhatTheRecordingRowFoldsToo(
        int networkId,
        string name,
        string summary,
        string extended,
        string folded)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, networkId, name, summary, extended);

        Assert.Equal(folded, await Scalar(connection, $"SELECT searchable FROM recording WHERE network_id = {networkId}"));
        Assert.Equal(
            folded,
            ProgrammeSearchText.Folded(
                name
                + ProgrammeSearchText.BetweenNameAndSummary
                + summary
                + ProgrammeSearchText.BetweenNameAndSummary
                + extended));
    }

    [Fact]
    public async Task ThePerformerNamesTheGuideKeepsInTheDetailAreSearchableToo()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 43021, "A programme", "What it is about", "◇出演者 …本名陽子…");

        Assert.Contains(
            "本名陽子",
            (string)(await Scalar(connection, "SELECT searchable FROM recording WHERE network_id = 43021"))!,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheColumnIsTheStoresOwnWorkSoNothingCanWriteAnAnswerIntoIt()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 43031, "A programme", string.Empty, string.Empty);

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => Execute(
            connection,
            "UPDATE recording SET searchable = 'anything' WHERE network_id = 43031"));

        Assert.Equal(WrittenToAGeneratedColumn, refusal.SqlState);
    }

    [Fact]
    public async Task TheOneIndexTheLibraryAddsSeeksBothWaysDownTheOrderItReadsIn()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            $"CREATE INDEX {RecordingConfiguration.LibraryIndexName} ON public.recording "
            + "USING btree (started_at_actual DESC, id DESC)",
            await Scalar(
                connection,
                "SELECT indexdef FROM pg_indexes WHERE tablename = 'recording' "
                + $"AND indexname = '{RecordingConfiguration.LibraryIndexName}'"));
    }

    [Fact]
    public async Task TheGenresRecordedWithTheProgrammeAreReadOutTheWayTheGuideReadsItsOwn()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, 43041, "A programme", string.Empty, string.Empty, """[{"kind": 8, "sort": 0}, {"kind": 1, "sort": 1}]""");

        Assert.Equal(
            "{8,1}",
            await Scalar(connection, "SELECT genre_kinds::text FROM recording WHERE network_id = 43041"));
    }

    [Fact]
    public async Task TheLibraryLeavesTheSearchExtensionsAndTheirIndexesToTheGuide()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        Assert.Equal(
            0L,
            await Scalar(
                connection,
                "SELECT count(*) FROM pg_indexes WHERE tablename = 'recording' "
                + "AND (indexdef LIKE '%gin%' OR indexdef LIKE '%trgm%')"));
    }

    private static async Task Record(
        NpgsqlConnection connection,
        int networkId,
        string name,
        string summary,
        string extended,
        string genres = "[]")
    {
        Guid id = Guid.NewGuid();

        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO recording (
                id, reservation_id, network_id, service_id, event_id, programme_start_at,
                output_root, file_name, file_size_observed, observed_at,
                started_at_actual, stopped_at_actual, aborted_at,
                written_duration_ms, resume_count, interruptions,
                expected_window_start, expected_window_end,
                recording_outcome, outcome_detail,
                scrambled_packets, eovf_count, measured_updated_at,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres, captured_at,
                broadcast_group_key, broadcast_group_role,
                cc_measured, cc_dropped_packets, cc_total_packets,
                pcr_anchor, drop_positions, pcr_reanchors, tuner_device_id, thumbnail_state)
            VALUES (
                '{id}', NULL, {networkId}, 1024, 4001, {Airs},
                'bulk', '{id:N}.m2ts', NULL, NULL,
                {Airs}, NULL, NULL,
                0, 0, '[]'::jsonb,
                {Airs}, {Ends},
                NULL, '[]'::jsonb,
                NULL, 0, NULL,
                @name, @summary, @extended, @genres::jsonb, {Now},
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb, 'pt3-0', 'Pending')
            """;
        command.Parameters.AddWithValue("name", name);
        command.Parameters.AddWithValue("summary", summary);
        command.Parameters.AddWithValue("extended", extended);
        command.Parameters.AddWithValue("genres", genres);

        await command.ExecuteNonQueryAsync();
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> Scalar(NpgsqlConnection connection, string sql)
    {
        await using NpgsqlCommand command = new(sql, connection);
        object? read = await command.ExecuteScalarAsync();

        return read is DBNull ? null : read;
    }
}
