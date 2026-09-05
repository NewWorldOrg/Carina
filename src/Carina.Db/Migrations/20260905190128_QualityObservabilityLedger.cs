using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class QualityObservabilityLedger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "quality_incident",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                detected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                breached = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                observed = table.Column<double>(type: "double precision", nullable: false),
                owner = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                classification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                notified_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                acknowledged_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                acknowledged_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                resolved_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                applied_current = table.Column<double>(type: "double precision", nullable: false),
                applied_default = table.Column<double>(type: "double precision", nullable: false),
                applied_observations = table.Column<long>(type: "bigint", nullable: false),
                applied_provisional = table.Column<bool>(type: "boolean", nullable: false),
                applied_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                subject_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                subject_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quality_incident", x => x.id);
                table.CheckConstraint("ck_quality_incident_applied", "applied_observations >= 0\nAND (applied_provisional OR applied_observations > 0)");
                table.CheckConstraint("ck_quality_incident_classification", "(owner = 'Quality') = (classification IS NULL)");
                table.CheckConstraint("ck_quality_incident_lifecycle", "((acknowledged_at IS NULL) = (acknowledged_by IS NULL))\nAND (acknowledged_at IS NULL OR notified_at IS NOT NULL)\nAND (notified_at IS NULL OR notified_at >= detected_at)\nAND (acknowledged_at IS NULL OR acknowledged_at >= notified_at)\nAND (resolved_at IS NULL OR resolved_at >= detected_at)\nAND ((state = 'Resolved') = (resolved_at IS NOT NULL))\nAND ((state = 'Acknowledged')\n    = (acknowledged_at IS NOT NULL AND resolved_at IS NULL))\nAND ((state = 'Notified')\n    = (notified_at IS NOT NULL AND acknowledged_at IS NULL AND resolved_at IS NULL))\nAND ((state = 'Detected')\n    = (notified_at IS NULL AND resolved_at IS NULL))");
                table.CheckConstraint("ck_quality_incident_vocabulary", "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Acknowledged', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream')");
            });

        migrationBuilder.CreateTable(
            name: "quality_session_measurement",
            columns: table => new
            {
                driver_instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tuner_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ended_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cc_measured = table.Column<bool>(type: "boolean", nullable: false),
                cc_dropped_packets = table.Column<long>(type: "bigint", nullable: true),
                cc_total_packets = table.Column<long>(type: "bigint", nullable: true),
                eovf_count = table.Column<long>(type: "bigint", nullable: false),
                measured_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quality_session_measurement", x => new { x.driver_instance_id, x.session_id });
                table.CheckConstraint("ck_quality_session_measurement_channel", "network_id BETWEEN 0 AND 65535\nAND service_id BETWEEN 0 AND 65535");
                table.CheckConstraint("ck_quality_session_measurement_counts", "(cc_measured = (cc_dropped_packets IS NOT NULL AND cc_total_packets IS NOT NULL))\nAND (cc_measured = (measured_updated_at IS NOT NULL))\nAND (cc_dropped_packets IS NULL OR cc_dropped_packets >= 0)\nAND (cc_total_packets IS NULL OR cc_total_packets >= 0)\nAND eovf_count >= 0");
                table.CheckConstraint("ck_quality_session_measurement_purpose", "purpose IN ('Unspecified', 'Recording', 'Live', 'Survey', 'Scan', 'SurveyNow')\nAND purpose <> 'Recording'");
                table.CheckConstraint("ck_quality_session_measurement_span", "ended_at IS NULL OR ended_at >= started_at");
            });

        migrationBuilder.CreateTable(
            name: "quality_signal_rollup",
            columns: table => new
            {
                granularity = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                tuner_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                samples = table.Column<long>(type: "bigint", nullable: false),
                locked = table.Column<long>(type: "bigint", nullable: false),
                unmeasured = table.Column<long>(type: "bigint", nullable: false),
                unreachable = table.Column<long>(type: "bigint", nullable: false),
                cnr_average = table.Column<double>(type: "double precision", nullable: true),
                cnr_lowest = table.Column<int>(type: "integer", nullable: true),
                cnr_highest = table.Column<int>(type: "integer", nullable: true),
                bit_errors = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quality_signal_rollup", x => new { x.granularity, x.window_start, x.tuner_device_id, x.network_id, x.service_id });
                table.CheckConstraint("ck_quality_signal_rollup_bit_errors", "jsonb_typeof(bit_errors) = 'array'");
                table.CheckConstraint("ck_quality_signal_rollup_carrier_to_noise", "((cnr_average IS NULL) = (cnr_lowest IS NULL))\nAND ((cnr_average IS NULL) = (cnr_highest IS NULL))\nAND (cnr_average IS NULL OR cnr_average BETWEEN cnr_lowest AND cnr_highest)");
                table.CheckConstraint("ck_quality_signal_rollup_channel", "network_id BETWEEN 0 AND 65535\nAND service_id BETWEEN 0 AND 65535");
                table.CheckConstraint("ck_quality_signal_rollup_counts", "samples >= 0\nAND locked BETWEEN 0 AND samples\nAND unmeasured >= 0\nAND unreachable >= 0");
                table.CheckConstraint("ck_quality_signal_rollup_granularity", "granularity IN ('Minute', 'Hour')");
            });

        migrationBuilder.CreateTable(
            name: "quality_signal_sample",
            columns: table => new
            {
                driver_instance_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                session_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                taken_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                tuner_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                bit_errors = table.Column<string>(type: "jsonb", nullable: false),
                bit_errors_read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                cnr_milli_decibels = table.Column<int>(type: "integer", nullable: true),
                cnr_read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                lock_read_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                locked = table.Column<bool>(type: "boolean", nullable: false),
                metrics_not_read = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quality_signal_sample", x => new { x.driver_instance_id, x.session_id, x.taken_at });
                table.CheckConstraint("ck_quality_signal_sample_channel", "network_id BETWEEN 0 AND 65535\nAND service_id BETWEEN 0 AND 65535");
                table.CheckConstraint("ck_quality_signal_sample_lock_gate", "locked\nOR (cnr_milli_decibels IS NULL AND bit_errors = '[]'::jsonb)");
                table.CheckConstraint("ck_quality_signal_sample_purpose", "purpose IN ('Unspecified', 'Recording', 'Live', 'Survey', 'Scan', 'SurveyNow')");
                table.CheckConstraint("ck_quality_signal_sample_read_at", "((cnr_milli_decibels IS NULL) = (cnr_read_at IS NULL))\nAND ((bit_errors = '[]'::jsonb) = (bit_errors_read_at IS NULL))\nAND jsonb_typeof(bit_errors) = 'array'\nAND jsonb_typeof(metrics_not_read) = 'array'");
            });

        migrationBuilder.CreateTable(
            name: "quality_threshold",
            columns: table => new
            {
                threshold_key = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                updated_by = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                current_value = table.Column<double>(type: "double precision", nullable: false),
                default_value = table.Column<double>(type: "double precision", nullable: false),
                observations = table.Column<long>(type: "bigint", nullable: false),
                provisional = table.Column<bool>(type: "boolean", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_quality_threshold", x => x.threshold_key);
                table.CheckConstraint("ck_quality_threshold_key", "threshold_key IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')");
                table.CheckConstraint("ck_quality_threshold_standing", "observations >= 0\nAND (provisional OR observations > 0)");
            });

        migrationBuilder.CreateIndex(
            name: "ix_quality_incident_unsettled",
            table: "quality_incident",
            column: "detected_at",
            filter: "resolved_at IS NULL AND acknowledged_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_quality_session_measurement_started_at",
            table: "quality_session_measurement",
            column: "started_at");

        migrationBuilder.CreateIndex(
            name: "ix_quality_signal_rollup_window_start",
            table: "quality_signal_rollup",
            columns: new[] { "granularity", "window_start" });

        migrationBuilder.CreateIndex(
            name: "ix_quality_signal_sample_taken_at",
            table: "quality_signal_sample",
            column: "taken_at");

        migrationBuilder.CreateIndex(
            name: "ix_quality_signal_sample_tuner_taken_at",
            table: "quality_signal_sample",
            columns: new[] { "tuner_device_id", "taken_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "quality_incident");

        migrationBuilder.DropTable(
            name: "quality_session_measurement");

        migrationBuilder.DropTable(
            name: "quality_signal_rollup");

        migrationBuilder.DropTable(
            name: "quality_signal_sample");

        migrationBuilder.DropTable(
            name: "quality_threshold");
    }
}
