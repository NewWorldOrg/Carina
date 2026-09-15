using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class WhenTooMuchIsLeftScrambledToWatch : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_change_key",
            table: "quality_threshold_change");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_key",
            table: "quality_threshold");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_change_key",
            table: "quality_threshold_change",
            sql: "threshold_key IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_key",
            table: "quality_threshold",
            sql: "threshold_key IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Acknowledged', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_change_key",
            table: "quality_threshold_change");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_key",
            table: "quality_threshold");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_change_key",
            table: "quality_threshold_change",
            sql: "threshold_key IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_key",
            table: "quality_threshold",
            sql: "threshold_key IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Acknowledged', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide')");
    }
}
