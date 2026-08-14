using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814

namespace Carina.Db.Migrations;

public partial class ChannelsAndScans : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "broadcast_service",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                category = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                discovered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_broadcast_service", x => new { x.network_id, x.service_id });
                table.CheckConstraint("ck_broadcast_service_category", "category IN ('Television', 'Radio', 'Data', 'OneSeg', 'Temporary', 'Other')");
            });

        migrationBuilder.CreateTable(
            name: "satellite_transport_stream",
            columns: table => new
            {
                bs_channel = table.Column<int>(type: "integer", nullable: false),
                relative_stream_number = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_satellite_transport_stream", x => new { x.bs_channel, x.relative_stream_number });
                table.CheckConstraint("ck_satellite_transport_stream_slot", "bs_channel BETWEEN 1 AND 23 AND bs_channel % 2 = 1 AND bs_channel NOT IN (7, 17)\nAND relative_stream_number BETWEEN 0 AND 7");
            });

        migrationBuilder.CreateTable(
            name: "scan_run",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                driver_instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scan_run", x => x.id);
                table.CheckConstraint("ck_scan_run_finished", "(state = 'Running') = (finished_at IS NULL)");
                table.CheckConstraint("ck_scan_run_reason", "state NOT IN ('Failed', 'Cancelled') OR (reason IS NOT NULL AND length(btrim(reason)) > 0)");
                table.CheckConstraint("ck_scan_run_state", "state IN ('Running', 'Completed', 'Failed', 'Cancelled', 'Interrupted')");
            });

        migrationBuilder.CreateTable(
            name: "candidate_channel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                tune_system = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                physical_channel = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: true),
                is_selected = table.Column<bool>(type: "boolean", nullable: false),
                selection_source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                selected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                selected_measured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                selected_locked = table.Column<bool>(type: "boolean", nullable: true),
                selected_cnr_milli_decibels = table.Column<int>(type: "integer", nullable: true),
                selected_post_viterbi_error_bits = table.Column<long>(type: "bigint", nullable: true),
                selected_post_viterbi_total_bits = table.Column<long>(type: "bigint", nullable: true),
                measured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                locked = table.Column<bool>(type: "boolean", nullable: true),
                cnr_milli_decibels = table.Column<int>(type: "integer", nullable: true),
                post_viterbi_error_bits = table.Column<long>(type: "bigint", nullable: true),
                post_viterbi_total_bits = table.Column<long>(type: "bigint", nullable: true),
                needs_revalidation = table.Column<bool>(type: "boolean", nullable: false),
                rotation_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                next_attempt_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                needs_attention_since = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                discovered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_candidate_channel", x => x.id);
                table.CheckConstraint("ck_candidate_channel_measurement_lock", "measured_at IS NULL OR locked OR cnr_milli_decibels IS NULL");
                table.CheckConstraint("ck_candidate_channel_rotation", "consecutive_failures >= 0\nAND (rotation_state <> 'NeedsAttention'\n     OR (needs_attention_since IS NOT NULL AND next_attempt_at IS NULL))\nAND (rotation_state <> 'BackingOff' OR next_attempt_at IS NOT NULL)\nAND (rotation_state <> 'Active'\n     OR (next_attempt_at IS NULL AND needs_attention_since IS NULL))");
                table.CheckConstraint("ck_candidate_channel_selection", "is_selected = (selection_source IS NOT NULL) AND is_selected = (selected_at IS NOT NULL)");
                table.CheckConstraint("ck_candidate_channel_selection_measurement_lock", "selected_measured_at IS NULL OR selected_locked OR selected_cnr_milli_decibels IS NULL");
                table.CheckConstraint("ck_candidate_channel_tuning", "(tune_system = 'IsdbT' AND physical_channel BETWEEN 13 AND 62 AND transport_stream_id IS NULL)\nOR (tune_system = 'IsdbSBs' AND physical_channel BETWEEN 1 AND 23 AND physical_channel % 2 = 1\n    AND physical_channel NOT IN (7, 17) AND transport_stream_id IS NOT NULL)\nOR (tune_system = 'IsdbSCs110' AND physical_channel BETWEEN 2 AND 24 AND physical_channel % 2 = 0\n    AND transport_stream_id IS NULL)");
                table.ForeignKey(
                    name: "fk_candidate_channel_broadcast_service_network_id_service_id",
                    columns: x => new { x.network_id, x.service_id },
                    principalTable: "broadcast_service",
                    principalColumns: new[] { "network_id", "service_id" },
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "scan_run_attempt",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                scan_run_id = table.Column<Guid>(type: "uuid", nullable: false),
                tune_system = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                physical_channel = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: true),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                measured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                locked = table.Column<bool>(type: "boolean", nullable: true),
                cnr_milli_decibels = table.Column<int>(type: "integer", nullable: true),
                post_viterbi_error_bits = table.Column<long>(type: "bigint", nullable: true),
                post_viterbi_total_bits = table.Column<long>(type: "bigint", nullable: true),
                observed_transport_stream_id = table.Column<int>(type: "integer", nullable: true),
                detail = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                finished_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_scan_run_attempt", x => x.id);
                table.CheckConstraint("ck_scan_run_attempt_measurement_lock", "measured_at IS NULL OR locked OR cnr_milli_decibels IS NULL");
                table.CheckConstraint("ck_scan_run_attempt_outcome", "outcome IN ('Succeeded', 'NoLock', 'LockedWithoutData', 'IncompleteTables', 'UnexpectedStream')");
                table.CheckConstraint("ck_scan_run_attempt_span", "finished_at >= started_at");
                table.CheckConstraint("ck_scan_run_attempt_tuning", "(tune_system = 'IsdbT' AND physical_channel BETWEEN 13 AND 62 AND transport_stream_id IS NULL)\nOR (tune_system = 'IsdbSBs' AND physical_channel BETWEEN 1 AND 23 AND physical_channel % 2 = 1\n    AND physical_channel NOT IN (7, 17) AND transport_stream_id IS NOT NULL)\nOR (tune_system = 'IsdbSCs110' AND physical_channel BETWEEN 2 AND 24 AND physical_channel % 2 = 0\n    AND transport_stream_id IS NULL)");
                table.ForeignKey(
                    name: "fk_scan_run_attempt_scan_run_scan_run_id",
                    column: x => x.scan_run_id,
                    principalTable: "scan_run",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.InsertData(
            table: "satellite_transport_stream",
            columns: new[] { "bs_channel", "relative_stream_number", "transport_stream_id" },
            values: new object[,]
            {
                { 1, 0, 16400 },
                { 3, 0, 16432 },
                { 5, 0, 16464 },
                { 9, 0, 16528 },
                { 11, 0, 16560 },
                { 13, 0, 16592 },
                { 15, 0, 16624 },
                { 19, 0, 16688 },
                { 21, 0, 16720 },
                { 23, 0, 16752 }
            });

        migrationBuilder.CreateIndex(
            name: "ix_broadcast_service_last_seen_at",
            table: "broadcast_service",
            column: "last_seen_at");

        migrationBuilder.CreateIndex(
            name: "ix_candidate_channel_rotation_state",
            table: "candidate_channel",
            column: "rotation_state");

        migrationBuilder.CreateIndex(
            name: "ux_candidate_channel_selected",
            table: "candidate_channel",
            columns: new[] { "network_id", "service_id" },
            unique: true,
            filter: "is_selected");

        migrationBuilder.CreateIndex(
            name: "ix_scan_run_started_at",
            table: "scan_run",
            column: "started_at");

        migrationBuilder.CreateIndex(
            name: "ux_scan_run_running",
            table: "scan_run",
            column: "state",
            unique: true,
            filter: "state = 'Running'");

        migrationBuilder.CreateIndex(
            name: "ix_scan_run_attempt_scan_run_id_outcome",
            table: "scan_run_attempt",
            columns: new[] { "scan_run_id", "outcome" });

        migrationBuilder.Sql(
            """
            CREATE UNIQUE INDEX ux_candidate_channel_identity
                ON candidate_channel (network_id, service_id, tune_system, physical_channel, transport_stream_id)
                NULLS NOT DISTINCT
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "candidate_channel");

        migrationBuilder.DropTable(
            name: "satellite_transport_stream");

        migrationBuilder.DropTable(
            name: "scan_run_attempt");

        migrationBuilder.DropTable(
            name: "broadcast_service");

        migrationBuilder.DropTable(
            name: "scan_run");
    }
}
