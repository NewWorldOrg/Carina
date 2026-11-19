using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class WhetherAnEncodeWasAskedFor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (string table in new[] { "rule", "reservation", "recording" })
        {
            migrationBuilder.AddColumn<bool>(
                name: "encode_when_recorded",
                table: table,
                type: "boolean",
                nullable: false,
                defaultValue: true);
        }

        Tick(migrationBuilder, ",\n    reservation.encode_when_recorded");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        Tick(migrationBuilder, string.Empty);

        foreach (string table in new[] { "rule", "reservation", "recording" })
        {
            migrationBuilder.DropColumn(name: "encode_when_recorded", table: table);
        }
    }

    private static void Tick(MigrationBuilder migrationBuilder, string asked)
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
                reservation.captured_at,
                reservation.margin_after{asked}
            FROM reservation
            WHERE reservation.recording_outcome IS NULL
              AND (reservation.started_at IS NOT NULL OR reservation.state = 'Scheduled');
            """);
    }
}
