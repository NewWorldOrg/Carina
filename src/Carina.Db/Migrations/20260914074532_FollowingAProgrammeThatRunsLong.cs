using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class FollowingAProgrammeThatRunsLong : Migration
{
    private const string Faults =
        "'TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', "
        + "'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow', 'NothingLanded', "
        + "'SizeUnobserved', 'StoppedUnasked', 'LighterThanTheStream', 'HeavierThanTheStream'";

    private const string FaultsAsJson =
        "\"TuneFailed\", \"RefusedByDiskPrecheck\", \"DiskExhausted\", \"DriverLost\", \"DrainGraceExpired\", "
        + "\"StoppedByHand\", \"TunerContended\", \"ScramblingUnresolved\", \"ShortOfTheWindow\", \"NothingLanded\", "
        + "\"SizeUnobserved\", \"StoppedUnasked\", \"LighterThanTheStream\", \"HeavierThanTheStream\"";

    private const string TheEndWasUndecided = ", 'EndStillUndecided'";

    private const string TheEndWasUndecidedAsJson = ", \"EndStillUndecided\"";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        Reasons(migrationBuilder, TheEndWasUndecided, TheEndWasUndecidedAsJson);
        Tick(migrationBuilder, ",\n    reservation.margin_after");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        Reasons(migrationBuilder, string.Empty, string.Empty);
        Tick(migrationBuilder, string.Empty);
    }

    private static void Reasons(MigrationBuilder migrationBuilder, string added, string addedAsJson)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_reservation_outcome_faults",
            table: "reservation_outcome");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_reservation_outcome_faults",
            table: "reservation_outcome",
            sql: $"faults <@ '[{FaultsAsJson}{addedAsJson}]'::jsonb\n"
                 + "AND (kind <> 'TuneFailure' OR faults @> '[\"TuneFailed\"]'::jsonb)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_reasons",
            table: "recording",
            sql: $"recording_reasons_hold(outcome_detail, ARRAY[{Faults}{added}]::text[], "
                 + "ARRAY['NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch']::text[], started_at_actual)");
    }

    private static void Tick(MigrationBuilder migrationBuilder, string margin)
    {
        migrationBuilder.Sql("DROP VIEW IF EXISTS reservation_recording_tick;");
        migrationBuilder.Sql(
            $"""
            CREATE VIEW reservation_recording_tick AS
            SELECT
                reservation.id,
                reservation.network_id,
                reservation.service_id,
                reservation.event_id,
                reservation.programme_start_at,
                reservation.snapshot_name,
                reservation.priority,
                reservation.broadcast_group_key,
                reservation.broadcast_group_role,
                reservation.start_at - make_interval(secs => reservation.margin_before) AS effective_start_at,
                reservation.end_at + make_interval(secs => reservation.margin_after) AS effective_end_at,
                reservation.end_at_confirmed,
                reservation.started_at,
                reservation.started_at IS NOT NULL AS in_flight,
                reservation.snapshot_summary,
                reservation.snapshot_extended,
                reservation.snapshot_genres,
                reservation.snapshot_audio,
                reservation.snapshot_sounds,
                reservation.captured_at{margin}
            FROM reservation
            WHERE reservation.recording_outcome IS NULL
              AND (reservation.started_at IS NOT NULL OR reservation.state = 'Scheduled');
            """);
    }
}
