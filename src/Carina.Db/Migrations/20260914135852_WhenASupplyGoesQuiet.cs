using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhenASupplyGoesQuiet : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_quality_incident_unsettled",
            table: "quality_incident");

        migrationBuilder.AddColumn<string>(
            name: "silence",
            table: "quality_incident",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_quality_incident_unsettled",
            table: "quality_incident",
            column: "detected_at",
            filter: "resolved_at IS NULL");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Acknowledged', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_silence",
            table: "quality_incident",
            sql: "(breached = 'SupplySilence') = (silence IS NOT NULL)\nAND (silence IS NULL OR silence IN ('RecordingProgress', 'RecordingMeasurement', 'SignalSamples', 'GuideVisits'))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_quality_incident_unsettled",
            table: "quality_incident");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_silence",
            table: "quality_incident");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Acknowledged', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream')");

        migrationBuilder.DropColumn(
            name: "silence",
            table: "quality_incident");

        migrationBuilder.CreateIndex(
            name: "ix_quality_incident_unsettled",
            table: "quality_incident",
            column: "detected_at",
            filter: "resolved_at IS NULL AND acknowledged_at IS NULL");
    }
}
