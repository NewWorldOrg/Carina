using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AnAnomalyStandsUntilItClears : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_lifecycle",
            table: "quality_incident");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.Sql(
            "UPDATE quality_incident SET state = 'Notified' WHERE state = 'Acknowledged';");

        migrationBuilder.DropColumn(
            name: "acknowledged_at",
            table: "quality_incident");

        migrationBuilder.DropColumn(
            name: "acknowledged_by",
            table: "quality_incident");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_lifecycle",
            table: "quality_incident",
            sql: "(notified_at IS NULL OR notified_at >= detected_at)\nAND (resolved_at IS NULL OR resolved_at >= detected_at)\nAND ((state = 'Resolved') = (resolved_at IS NOT NULL))\nAND ((state = 'Notified')\n    = (notified_at IS NOT NULL AND resolved_at IS NULL))\nAND ((state = 'Detected')\n    = (notified_at IS NULL AND resolved_at IS NULL))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_lifecycle",
            table: "quality_incident");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AddColumn<DateTime>(
            name: "acknowledged_at",
            table: "quality_incident",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "acknowledged_by",
            table: "quality_incident",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_lifecycle",
            table: "quality_incident",
            sql: "((acknowledged_at IS NULL) = (acknowledged_by IS NULL))\nAND (acknowledged_at IS NULL OR notified_at IS NOT NULL)\nAND (notified_at IS NULL OR notified_at >= detected_at)\nAND (acknowledged_at IS NULL OR acknowledged_at >= notified_at)\nAND (resolved_at IS NULL OR resolved_at >= detected_at)\nAND ((state = 'Resolved') = (resolved_at IS NOT NULL))\nAND ((state = 'Acknowledged')\n    = (acknowledged_at IS NOT NULL AND resolved_at IS NULL))\nAND ((state = 'Notified')\n    = (notified_at IS NOT NULL AND acknowledged_at IS NULL AND resolved_at IS NULL))\nAND ((state = 'Detected')\n    = (notified_at IS NULL AND resolved_at IS NULL))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Acknowledged', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide')");
    }
}
