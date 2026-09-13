using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheSoundARecordingWasPromised : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        foreach (string table in new[] { "reservation", "recording" })
        {
            migrationBuilder.AddColumn<string>(
                name: "snapshot_audio",
                table: table,
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Undetermined");

            migrationBuilder.Sql($"ALTER TABLE {table} ALTER COLUMN snapshot_audio DROP DEFAULT");

            migrationBuilder.AddColumn<int>(
                name: "snapshot_sounds",
                table: table,
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql($"ALTER TABLE {table} ALTER COLUMN snapshot_sounds DROP DEFAULT");

            migrationBuilder.AddCheckConstraint(
                name: $"ck_{table}_snapshot_audio",
                table: table,
                sql: "snapshot_audio IN ('Undetermined', 'Mono', 'Stereo', 'DualMono', 'Surround')");

            migrationBuilder.AddCheckConstraint(
                name: $"ck_{table}_snapshot_sounds",
                table: table,
                sql: "snapshot_sounds >= 0");
        }

        migrationBuilder.Sql("DROP VIEW IF EXISTS reservation_recording_tick;");
        migrationBuilder.Sql(
            """
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
                reservation.captured_at
            FROM reservation
            WHERE reservation.recording_outcome IS NULL
              AND (reservation.started_at IS NOT NULL OR reservation.state = 'Scheduled');
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql("DROP VIEW IF EXISTS reservation_recording_tick;");
        migrationBuilder.Sql(
            """
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
                reservation.captured_at
            FROM reservation
            WHERE reservation.recording_outcome IS NULL
              AND (reservation.started_at IS NOT NULL OR reservation.state = 'Scheduled');
            """);

        foreach (string table in new[] { "reservation", "recording" })
        {
            migrationBuilder.DropCheckConstraint(name: $"ck_{table}_snapshot_audio", table: table);
            migrationBuilder.DropCheckConstraint(name: $"ck_{table}_snapshot_sounds", table: table);
            migrationBuilder.DropColumn(name: "snapshot_audio", table: table);
            migrationBuilder.DropColumn(name: "snapshot_sounds", table: table);
        }
    }
}
