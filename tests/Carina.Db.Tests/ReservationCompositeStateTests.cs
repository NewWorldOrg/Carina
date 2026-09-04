using Carina.Domain.Base;
using Carina.Domain.Channels;
using Carina.Domain.Programmes;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;

using Npgsql;

namespace Carina.Db.Tests;

/// <summary>
/// The stored composite column and <see cref="Reservation.Standing"/> are two writings of one
/// derivation — a generated column cannot call into the domain, so the entity has to say the same
/// thing a second time. They are held equal by measurement rather than by intention: every
/// combination the table can hold is pushed through both and the two answers are compared.
/// </summary>
[Collection(ConnectionEnvironmentCollection.Name)]
[Trait("Category", "DbIntegration")]
public sealed class ReservationCompositeStateTests(MigratedScratchDatabase database)
    : IClassFixture<MigratedScratchDatabase>
{
    private const string Airs = "timestamptz '2026-08-24 20:00:00+00'";

    private const string Ends = "timestamptz '2026-08-24 21:00:00+00'";

    private const string Claimed = "timestamptz '2026-08-24 19:59:00+00'";

    private static readonly DateTime Now = new(2026, 8, 24, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task TheStoredCompositeAndTheEntityGiveTheSameAnswerForEveryCombinationTheTableHolds()
    {
        await using NpgsqlConnection connection = await database.OpenAsync();
        var disagreed = new List<string>();
        int eventId = 5000;

        foreach ((ReservationState state, DateTime? startedAt, RecordingOutcome? outcome) in Combinations())
        {
            await Reserve(connection, eventId, state, startedAt, outcome);

            string stored = await Composite(connection, eventId);
            string entity = Rehydrated(state, startedAt, outcome).Standing.ToString();

            if (!string.Equals(stored, entity, StringComparison.Ordinal))
            {
                disagreed.Add(
                    $"state={state}, claimed={startedAt is not null}, outcome={outcome?.ToString() ?? "none"}: "
                    + $"the column says {stored} and the entity says {entity}");
            }

            eventId++;
        }

        Assert.Empty(disagreed);
        Assert.Equal(20, eventId - 5000);
    }

    private static IEnumerable<(ReservationState, DateTime?, RecordingOutcome?)> Combinations()
    {
        foreach (ReservationState state in Enum.GetValues<ReservationState>())
        {
            yield return (state, null, null);
            yield return (state, Now, null);

            foreach (RecordingOutcome outcome in Enum.GetValues<RecordingOutcome>())
            {
                yield return (state, Now, outcome);
            }
        }
    }

    private static Reservation Rehydrated(
        ReservationState state,
        DateTime? startedAt,
        RecordingOutcome? outcome)
        => Reservation.Rehydrate(
            ReservationId.New(),
            new ProgrammeRef(new NetworkId(60100), new ServiceId(1024), new EventId(4001), Now.AddHours(8)),
            null,
            new Priority(10),
            Now.AddHours(8),
            Now.AddHours(9),
            true,
            Margin.OfSeconds(10),
            Margin.OfSeconds(30),
            new ProgrammeSnapshot("A programme", "What it is about", string.Empty, [], Now),
            null,
            BroadcastGroupRole.Standalone,
            state,
            startedAt,
            outcome,
            false,
            [],
            false,
            null,
            false,
            null,
            Now);

    private static async Task Reserve(
        NpgsqlConnection connection,
        int eventId,
        ReservationState state,
        DateTime? startedAt,
        RecordingOutcome? outcome)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO reservation (
                id, network_id, service_id, event_id, programme_start_at, rule_id, priority,
                start_at, end_at, end_at_confirmed, margin_before, margin_after,
                snapshot_name, snapshot_summary, snapshot_extended, snapshot_genres, captured_at,
                epg_diverged, epg_diverged_detail, epg_missing, acknowledged_at,
                reception_unavailable, reception_unavailable_since,
                broadcast_group_key, broadcast_group_role, state, started_at, recording_outcome, created_at)
            VALUES (
                '{Guid.NewGuid()}', 60100, 1024, {eventId}, {Airs}, NULL, 10,
                {Airs}, {Ends}, true, 10, 30,
                'A programme', 'What it is about', '', '[]'::jsonb, {Airs},
                false, '[]'::jsonb, false, NULL, false, NULL,
                NULL, 'Standalone', '{state}', {(startedAt is null ? "NULL" : Claimed)},
                {(outcome is null ? "NULL" : $"'{outcome}'")}, {Airs})
            """;

        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> Composite(NpgsqlConnection connection, int eventId)
    {
        await using NpgsqlCommand command = connection.CreateCommand();
        command.CommandText =
            $"SELECT composite_state FROM reservation WHERE network_id = 60100 AND event_id = {eventId}";

        return (string)(await command.ExecuteScalarAsync())!;
    }
}
