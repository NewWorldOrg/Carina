using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class Reservations : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "reservation_outcome",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                programme_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                snapshot_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                effective_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                effective_end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tune_failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                recording_outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                recorded_instead = table.Column<string>(type: "jsonb", nullable: false),
                occurred_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reservation_outcome", x => x.id);
                table.CheckConstraint("ck_reservation_outcome_kind", "kind IN ('Competing', 'Missed', 'TuneFailure', 'RecordingFailure')");
                table.CheckConstraint("ck_reservation_outcome_recorded_instead", "kind = 'Competing' OR jsonb_array_length(recorded_instead) = 0");
                table.CheckConstraint("ck_reservation_outcome_recording_outcome", "(recording_outcome IS NULL OR recording_outcome IN ('Complete', 'Truncated', 'Failed'))\nAND (kind <> 'RecordingFailure' OR recording_outcome IS NOT NULL)");
                table.CheckConstraint("ck_reservation_outcome_tune_failure", "(tune_failure IS NULL\n OR tune_failure IN ('NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch'))\nAND (kind <> 'TuneFailure' OR tune_failure IS NOT NULL)");
                table.CheckConstraint("ck_reservation_outcome_window", "effective_end_at > effective_start_at");
            });

        migrationBuilder.CreateTable(
            name: "rule",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                query = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                priority = table.Column<int>(type: "integer", nullable: false),
                enabled = table.Column<bool>(type: "boolean", nullable: false),
                margin_before = table.Column<int>(type: "integer", nullable: false),
                margin_after = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_rule", x => x.id);
                table.CheckConstraint("ck_rule_margins", "margin_before BETWEEN 0 AND 3600 AND margin_after BETWEEN 0 AND 3600");
                table.CheckConstraint("ck_rule_priority", "priority BETWEEN 1 AND 99");
                table.CheckConstraint("ck_rule_query", "length(btrim(query)) > 0");
            });

        migrationBuilder.CreateTable(
            name: "reservation",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                programme_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                rule_id = table.Column<Guid>(type: "uuid", nullable: true),
                priority = table.Column<int>(type: "integer", nullable: false),
                start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                end_at_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                margin_before = table.Column<int>(type: "integer", nullable: false),
                margin_after = table.Column<int>(type: "integer", nullable: false),
                snapshot_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                snapshot_summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                snapshot_extended = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                snapshot_genres = table.Column<string>(type: "jsonb", nullable: false),
                captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                epg_diverged = table.Column<bool>(type: "boolean", nullable: false),
                epg_diverged_detail = table.Column<string>(type: "jsonb", nullable: false),
                epg_missing = table.Column<bool>(type: "boolean", nullable: false),
                acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                broadcast_group_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                broadcast_group_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                recording_outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                composite_state = table.Column<string>(type: "text", nullable: true, computedColumnSql: "CASE\n    WHEN recording_outcome IS NOT NULL THEN recording_outcome\n    WHEN started_at IS NOT NULL THEN 'Recording'\n    ELSE state\nEND", stored: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_reservation", x => x.id);
                table.CheckConstraint("ck_reservation_broadcast_group", "broadcast_group_role IN ('Standalone', 'MovementPrimary', 'MovementSuppressed', 'RelaySegment')\nAND (broadcast_group_role = 'Standalone' OR broadcast_group_key IS NOT NULL)");
                table.CheckConstraint("ck_reservation_divergence", "epg_diverged = (jsonb_array_length(epg_diverged_detail) > 0)\nAND (acknowledged_at IS NULL OR epg_diverged OR epg_missing)");
                table.CheckConstraint("ck_reservation_margins", "margin_before BETWEEN 0 AND 3600 AND margin_after BETWEEN 0 AND 3600");
                table.CheckConstraint("ck_reservation_priority", "priority BETWEEN 1 AND 99");
                table.CheckConstraint("ck_reservation_recording_outcome", "recording_outcome IS NULL\nOR (recording_outcome IN ('Complete', 'Truncated', 'Failed') AND started_at IS NOT NULL)");
                table.CheckConstraint("ck_reservation_state", "state IN ('Scheduled', 'Conflict', 'Cancelled', 'Missed')");
                table.CheckConstraint("ck_reservation_window", "end_at > start_at");
                table.ForeignKey(
                    name: "fk_reservation_rule_rule_id",
                    column: x => x.rule_id,
                    principalTable: "rule",
                    principalColumn: "id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "ix_reservation_broadcast_group",
            table: "reservation",
            column: "broadcast_group_key");

        migrationBuilder.CreateIndex(
            name: "ix_reservation_claimable",
            table: "reservation",
            column: "start_at",
            filter: "started_at IS NULL AND state = 'Scheduled'");

        migrationBuilder.CreateIndex(
            name: "ix_reservation_rule_id",
            table: "reservation",
            column: "rule_id");

        migrationBuilder.CreateIndex(
            name: "ix_reservation_window",
            table: "reservation",
            columns: new[] { "start_at", "end_at" },
            filter: "state IN ('Scheduled', 'Conflict')");

        migrationBuilder.CreateIndex(
            name: "ux_reservation_programme",
            table: "reservation",
            columns: new[] { "network_id", "service_id", "event_id", "programme_start_at" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_reservation_outcome_occurred_at",
            table: "reservation_outcome",
            column: "occurred_at");

        migrationBuilder.CreateIndex(
            name: "ix_reservation_outcome_reservation",
            table: "reservation_outcome",
            column: "reservation_id");

        migrationBuilder.CreateIndex(
            name: "ix_rule_precedence",
            table: "rule",
            columns: new[] { "priority", "created_at", "id" },
            descending: new[] { true, false, false },
            filter: "enabled");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "reservation");

        migrationBuilder.DropTable(
            name: "reservation_outcome");

        migrationBuilder.DropTable(
            name: "rule");
    }
}
