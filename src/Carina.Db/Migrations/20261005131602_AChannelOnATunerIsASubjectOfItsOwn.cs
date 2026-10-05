using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AChannelOnATunerIsASubjectOfItsOwn : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AlterColumn<string>(
            name: "subject_key",
            table: "quality_incident",
            type: "character varying(80)",
            maxLength: 80,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(64)",
            oldMaxLength: 64);

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide', 'Reception')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident");

        migrationBuilder.AlterColumn<string>(
            name: "subject_key",
            table: "quality_incident",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(80)",
            oldMaxLength: 80);

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_incident_vocabulary",
            table: "quality_incident",
            sql: "breached IN ('PacketsLostWarning', 'PacketsLostUnwatchable', 'PacketsLeftScrambled', 'Overflows', 'LockRate', 'CarrierToNoiseFloor', 'BitErrorRateCeiling', 'SupplySilence', 'PacketsLeftScrambledUnwatchable')\nAND owner IN ('Quality', 'Tuner', 'Guide', 'Reservation', 'Recording')\nAND state IN ('Detected', 'Notified', 'Resolved')\nAND subject_kind IN ('Tuner', 'Channel', 'Recording', 'TransportStream', 'Guide')");
    }
}
