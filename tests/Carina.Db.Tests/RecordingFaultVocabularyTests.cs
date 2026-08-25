using Carina.Domain.Recordings;

using Npgsql;

namespace Carina.Db.Tests;

[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class RecordingFaultVocabularyTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Now = "timestamptz '2026-08-24 12:00:00+00'";

    public static TheoryData<string> Faults => Named(Enum.GetValues<RecordingFault>());

    public static TheoryData<string> BreakingFaults => Named(RecordingFaults.ThatCanInterrupt);

    public static TheoryData<string> ConcludingFaults
        => Named(Enum.GetValues<RecordingFault>().Except(RecordingFaults.ThatCanInterrupt));

    [Theory]
    [MemberData(nameof(Faults))]
    public async Task EveryFaultTheApplicationCanNameIsOneTheLedgerTakes(string fault)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(connection, Guid.NewGuid(), Reason(fault));
    }

    [Theory]
    [MemberData(nameof(BreakingFaults))]
    public async Task EveryFaultThatBreaksARecordingIsOneAnInterruptionMayCarry(string fault)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        await Record(
            connection,
            Guid.NewGuid(),
            Reason("ShortOfTheWindow"),
            $$"""'[{"fault":"{{fault}}","occurredAt":"2026-08-24T20:10:00Z","resumedAt":null}]'::jsonb""");
    }

    [Theory]
    [MemberData(nameof(ConcludingFaults))]
    public async Task AFaultOnlyTheCrossCheckCanNameIsRefusedInTheHistory(string fault)
    {
        await using NpgsqlConnection connection = await database.OpenAsync();

        PostgresException refusal = await Assert.ThrowsAsync<PostgresException>(() => Record(
            connection,
            Guid.NewGuid(),
            Reason("ShortOfTheWindow"),
            $$"""'[{"fault":"{{fault}}","occurredAt":"2026-08-24T20:10:00Z","resumedAt":null}]'::jsonb"""));

        Assert.Equal(PostgresErrorCodes.CheckViolation, refusal.SqlState);
        Assert.Equal("ck_recording_history", refusal.ConstraintName);
    }

    private static TheoryData<string> Named(IEnumerable<RecordingFault> faults)
    {
        var named = new TheoryData<string>();
        foreach (RecordingFault fault in faults)
        {
            named.Add(fault.ToString());
        }

        return named;
    }

    private static string Reason(string fault)
        => $$"""'[{"fault":"{{fault}}","tuneFailure":null,"note":"","noticedAt":"2026-08-24T20:00:00Z"}]'::jsonb""";

    private static Task Record(
        NpgsqlConnection connection,
        Guid id,
        string detail,
        string interruptions = "'[]'::jsonb")
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
                '{id}', NULL, 47101, 1024, 4001, {Airs},
                'bulk', '{id:N}.m2ts', 1000, {Ends},
                {Airs}, {Ends}, {Ends},
                0, 0, {interruptions},
                {Airs}, {Ends},
                'Truncated', {detail},
                0, 0, NULL,
                'A programme', 'What it is about', '', '[]'::jsonb, {Now},
                NULL, 'Standalone',
                false, NULL, NULL,
                NULL, '[]'::jsonb, '[]'::jsonb, 'pt3-0', 'Pending')
            """,
            connection);

        return command.ExecuteNonQueryAsync();
    }
}
