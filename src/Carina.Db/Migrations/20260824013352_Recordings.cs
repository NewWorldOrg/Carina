using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class Recordings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "recording",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                programme_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                output_root = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                file_size_observed = table.Column<long>(type: "bigint", nullable: true),
                observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                started_at_actual = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                stopped_at_actual = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                aborted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                written_duration_ms = table.Column<long>(type: "bigint", nullable: false),
                resume_count = table.Column<int>(type: "integer", nullable: false),
                interruptions = table.Column<string>(type: "jsonb", nullable: false),
                expected_window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                expected_window_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                recording_outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                outcome_detail = table.Column<string>(type: "jsonb", nullable: false),
                scrambled_packets = table.Column<long>(type: "bigint", nullable: true),
                eovf_count = table.Column<long>(type: "bigint", nullable: false),
                measured_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                snapshot_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                snapshot_summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                snapshot_extended = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                snapshot_genres = table.Column<string>(type: "jsonb", nullable: false),
                captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                broadcast_group_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                broadcast_group_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                cc_dropped_packets = table.Column<long>(type: "bigint", nullable: true),
                cc_measured = table.Column<bool>(type: "boolean", nullable: false),
                cc_total_packets = table.Column<long>(type: "bigint", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_recording", x => x.id);
                table.CheckConstraint("ck_recording_complete_was_asked_for", "recording_outcome IS DISTINCT FROM 'Complete' OR aborted_at IS NOT NULL");
                table.CheckConstraint("ck_recording_counts", "written_duration_ms >= 0\nAND resume_count >= 0\nAND eovf_count >= 0\nAND (file_size_observed IS NULL OR file_size_observed >= 0)\nAND (scrambled_packets IS NULL OR scrambled_packets >= 0)");
                table.CheckConstraint("ck_recording_empty_file_failed", "recording_outcome IS NULL OR file_size_observed <> 0 OR recording_outcome = 'Failed'");
                table.CheckConstraint("ck_recording_measurement", "(cc_measured\n    OR (cc_dropped_packets IS NULL AND cc_total_packets IS NULL))\nAND (NOT cc_measured\n    OR (cc_dropped_packets IS NOT NULL\n        AND cc_total_packets IS NOT NULL\n        AND cc_dropped_packets <= cc_total_packets\n        AND measured_updated_at IS NOT NULL))");
                table.CheckConstraint("ck_recording_observation", "(file_size_observed IS NULL) = (observed_at IS NULL)");
                table.CheckConstraint("ck_recording_outcome", "(recording_outcome IS NULL OR recording_outcome IN ('Complete', 'Truncated', 'Failed'))\nAND (recording_outcome IS NULL OR stopped_at_actual IS NOT NULL)\nAND (recording_outcome IS NULL OR file_size_observed IS NOT NULL)");
                table.CheckConstraint("ck_recording_outcome_detail", "recording_outcome IS NULL\nOR recording_outcome = 'Complete'\nOR jsonb_array_length(outcome_detail) > 0");
                table.CheckConstraint("ck_recording_window", "expected_window_end > expected_window_start");
            });

        migrationBuilder.CreateIndex(
            name: "ix_recording_cc_dropped",
            table: "recording",
            column: "cc_dropped_packets",
            filter: "cc_measured AND cc_dropped_packets > 0");

        migrationBuilder.CreateIndex(
            name: "ix_recording_in_flight",
            table: "recording",
            column: "started_at_actual",
            filter: "recording_outcome IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_recording_reservation",
            table: "recording",
            column: "reservation_id",
            filter: "reservation_id IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "ix_recording_settled",
            table: "recording",
            columns: new[] { "recording_outcome", "stopped_at_actual" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "recording");
    }
}
