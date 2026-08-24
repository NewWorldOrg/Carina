using Carina.Domain.Recordings;
using Carina.Infrastructure.Persistence;

using Microsoft.EntityFrameworkCore;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingReadBackTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    public static TheoryData<string, string, string, string, int> Shapes => new()
    {
        { "'[]'::jsonb", "'[]'::jsonb", "'[]'::jsonb", "'[]'::jsonb", 47001 },
        {
            """'[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00Z","resumedAt":"2026-08-24T20:11:00Z"}]'::jsonb""",
            """'[{"fault":"ScramblingUnresolved","tuneFailure":null,"note":"card","noticedAt":"2026-08-24T20:12:00Z"}]'::jsonb""",
            """'[{"second":0,"continuity":1,"scrambled":0}]'::jsonb""",
            """'[{"second":0,"before":0,"after":8589934591}]'::jsonb""",
            47002
        },
        {
            """
            '[{"fault":"DriverLost","occurredAt":"2026-08-24T20:10:00.1234567Z","resumedAt":null}]'::jsonb
            """,
            """'[{"fault":"TuneFailed","tuneFailure":"IncompletePsi","note":"","noticedAt":"2026-08-24T20:00:00Z"}]'::jsonb""",
            """'[{"second":2147483647,"continuity":0,"scrambled":1}]'::jsonb""",
            """'[{"second":1,"before":8589934591,"after":0},{"second":2,"before":5,"after":6}]'::jsonb""",
            47003
        },
    };

    [Theory]
    [MemberData(nameof(Shapes))]
    public async Task EveryRowTheDatabaseTakesIsARowTheApplicationCanRead(
        string interruptions,
        string detail,
        string positions,
        string reanchors,
        int networkId)
    {
        var id = Guid.NewGuid();
        int resumes = interruptions.Contains("\"resumedAt\":\"", StringComparison.Ordinal) ? 1 : 0;

        await using (NpgsqlConnection connection = await database.OpenAsync())
        {
            await Record(connection, id, networkId, interruptions, resumes, detail, positions, reanchors);
        }

        await using CarinaDbContext context = CarinaDbContextFactory.Create(database.ConnectionString);
        var recordingId = new RecordingId(id);

        Recording read = await context.Set<Recording>().SingleAsync(entity => entity.Id == recordingId);

        Assert.Equal(networkId, read.NetworkId.Value);
        Assert.All(read.Interruptions, interruption => Assert.Equal(DateTimeKind.Utc, interruption.OccurredAt.Kind));
        Assert.All(read.OutcomeDetail, reason => Assert.Equal(DateTimeKind.Utc, reason.NoticedAt.Kind));
        Assert.Equal(resumes, read.ResumeCount);
        Assert.True(read.Positions.Located);
        Assert.NotNull(read.Counters.Dropped);
    }

    [Theory]
    [InlineData("network_id", "65536")]
    [InlineData("network_id", "-1")]
    [InlineData("service_id", "65536")]
    [InlineData("event_id", "0")]
    [InlineData("event_id", "65535")]
    public async Task AnIdentifierTheApplicationCouldNotReadBackIsRefused(string column, string value)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var id = Guid.NewGuid();

        await Record(connection, id, 47011, "'[]'::jsonb", 0, "'[]'::jsonb", "'[]'::jsonb", "'[]'::jsonb");

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(
            () => Execute(connection, $"UPDATE recording SET {column} = {value} WHERE id = '{id}'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_identifiers", refusal.ConstraintName);
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static Task Record(
        NpgsqlConnection connection,
        Guid id,
        int networkId,
        string interruptions,
        int resumes,
        string detail,
        string positions,
        string reanchors)
    {
        var command = new NpgsqlCommand(
            $"""
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
                0, {resumes}, {interruptions},
                {Airs}, {Ends},
                NULL, {detail},
                1000, 0, {Ends},
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                NULL, 'Standalone',
                true, 1000, 100000,
                900000, {positions}, {reanchors}, 'pt3-0', 'Pending')
            """,
            connection);

        return command.ExecuteNonQueryAsync();
    }
}
