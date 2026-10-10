using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ADataBroadcastStateForEveryRecording : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "data_broadcast_attempts",
            table: "recording",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "data_broadcast_made_at",
            table: "recording",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "data_broadcast_modules",
            table: "recording",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "data_broadcast_state",
            table: "recording",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "None");

        migrationBuilder.Sql("UPDATE recording SET data_broadcast_state = 'Coming' WHERE recording_outcome IS NOT NULL;");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_data_broadcast",
            table: "recording",
            sql: "data_broadcast_state IN ('None', 'Coming', 'Made', 'Missing', 'Failed')\nAND (data_broadcast_state = 'None' OR recording_outcome IS NOT NULL)\nAND (data_broadcast_state IN ('None', 'Coming')) = (data_broadcast_made_at IS NULL)\nAND (data_broadcast_state = 'Made') = (data_broadcast_modules IS NOT NULL)\nAND (data_broadcast_modules IS NULL OR data_broadcast_modules > 0)\nAND (data_broadcast_state <> 'Failed' OR data_broadcast_attempts > 0)\nAND (data_broadcast_state IN ('Failed', 'Coming') OR data_broadcast_attempts = 0)\nAND data_broadcast_attempts >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_data_broadcast",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "data_broadcast_attempts",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "data_broadcast_made_at",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "data_broadcast_modules",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "data_broadcast_state",
            table: "recording");
    }
}
