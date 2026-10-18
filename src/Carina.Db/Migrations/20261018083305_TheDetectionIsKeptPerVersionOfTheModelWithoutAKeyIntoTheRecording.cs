using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheDetectionIsKeptPerVersionOfTheModelWithoutAKeyIntoTheRecording : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "segment_model_version",
            columns: table => new
            {
                number = table.Column<int>(type: "integer", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                by_hand = table.Column<bool>(type: "boolean", nullable: false),
                kinds = table.Column<short>(type: "smallint", nullable: false),
                weights = table.Column<string>(type: "jsonb", nullable: false),
                station_offsets = table.Column<string>(type: "jsonb", nullable: false),
                series_offsets = table.Column<string>(type: "jsonb", nullable: false),
                thresholds = table.Column<string>(type: "jsonb", nullable: false),
                standardisation = table.Column<string>(type: "jsonb", nullable: false),
                trained_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                adopted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                retired_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                chosen_by_hand_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                rolled_back_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                evidence = table.Column<string>(type: "jsonb", nullable: true),
                metrics = table.Column<string>(type: "jsonb", nullable: true),
                recommended_threshold = table.Column<decimal>(type: "numeric(3,2)", nullable: true),
                rollback = table.Column<string>(type: "jsonb", nullable: true),
                stations = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_model_version", x => x.number);
                table.CheckConstraint("ck_segment_model_version_by_hand", "NOT by_hand OR (state = 'Active' AND chosen_by_hand_at IS NOT NULL)");
                table.CheckConstraint("ck_segment_model_version_evaluation", "(metrics IS NULL OR jsonb_typeof(metrics) = 'object')\nAND (stations IS NULL OR jsonb_typeof(stations) = 'array')\nAND (evidence IS NULL OR jsonb_typeof(evidence) = 'object')\nAND (rollback IS NULL OR jsonb_typeof(rollback) = 'object')\nAND (recommended_threshold IS NULL OR recommended_threshold BETWEEN 0 AND 1)");
                table.CheckConstraint("ck_segment_model_version_kinds", "kinds IN (2, 4)");
                table.CheckConstraint("ck_segment_model_version_number", "number >= 1");
                table.CheckConstraint("ck_segment_model_version_parameters", "jsonb_typeof(weights) = 'object'\nAND jsonb_typeof(station_offsets) = 'object'\nAND jsonb_typeof(series_offsets) = 'object'\nAND jsonb_typeof(thresholds) = 'object'\nAND jsonb_typeof(standardisation) = 'object'");
                table.CheckConstraint("ck_segment_model_version_rollback", "(rolled_back_at IS NULL) = (rollback IS NULL) AND (rolled_back_at IS NULL OR state = 'Retired')");
                table.CheckConstraint("ck_segment_model_version_state", "state IN ('Candidate', 'Active', 'Retired')");
                table.CheckConstraint("ck_segment_model_version_times", "(adopted_at IS NULL) = (state = 'Candidate')\nAND (retired_at IS NOT NULL) = (state = 'Retired')\nAND (adopted_at IS NULL OR adopted_at >= trained_at)\nAND (retired_at IS NULL OR retired_at >= adopted_at)");
            });

        migrationBuilder.CreateTable(
            name: "segment_detection",
            columns: table => new
            {
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                model_version = table.Column<int>(type: "integer", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                segments = table.Column<string>(type: "jsonb", nullable: false),
                cm_share = table.Column<double>(type: "double precision", nullable: false),
                snaps = table.Column<string>(type: "jsonb", nullable: false),
                extraction_version = table.Column<int>(type: "integer", nullable: false),
                catalog_until = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_detection", x => new { x.recording_id, x.model_version });
                table.CheckConstraint("ck_segment_detection_cm_share", "cm_share BETWEEN 0 AND 1");
                table.CheckConstraint("ck_segment_detection_extraction_version", "extraction_version >= 1");
                table.CheckConstraint("ck_segment_detection_segments", "jsonb_typeof(segments) = 'array'\nAND jsonb_array_length(segments) >= 1\nAND (state <> 'Marked' OR jsonb_array_length(segments) >= 2)\nAND jsonb_typeof(snaps) = 'array'");
                table.CheckConstraint("ck_segment_detection_state", "state IN ('Marked', 'NothingFound', 'Discarded')");
                table.ForeignKey(
                    name: "fk_segment_detection_segment_model_version_model_version",
                    column: x => x.model_version,
                    principalTable: "segment_model_version",
                    principalColumn: "number",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_segment_detection_detected_at",
            table: "segment_detection",
            column: "detected_at");

        migrationBuilder.CreateIndex(
            name: "ix_segment_detection_model_version",
            table: "segment_detection",
            column: "model_version");

        migrationBuilder.CreateIndex(
            name: "ux_segment_model_version_active",
            table: "segment_model_version",
            column: "state",
            unique: true,
            filter: "state = 'Active'");

        migrationBuilder.Sql(
            """
            INSERT INTO segment_model_version (number, state, by_hand, kinds, weights, station_offsets, series_offsets, thresholds, standardisation, trained_at, adopted_at)
            VALUES (
                1,
                'Active',
                false,
                2,
                '{"classes": {"Break": {"bias": -2.5, "features": {"isMultipleOf15s": 2.0, "edgeSilence": 1.5, "logoShare": -3.0, "catalogCmShare": 4.0}}}}',
                '{}',
                '{}',
                '{"silenceDbfs": -50, "blackLevel": 0.10, "sceneChange": 0.30, "gridToleranceSec": 1.0, "gridPenalty": -3.0, "lambda": 1, "lambdaStation": 10, "lambdaSeries": 10}',
                '{"isMultipleOf15s": {"mean": 0, "sd": 1}, "edgeSilence": {"mean": 0, "sd": 1}, "logoShare": {"mean": 0, "sd": 1}, "catalogCmShare": {"mean": 0, "sd": 1}}',
                now(),
                now())
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "segment_detection");

        migrationBuilder.DropTable(
            name: "segment_model_version");
    }
}
