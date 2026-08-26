using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class RecordingThumbnailFaults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_thumbnail",
            table: "recording");

        migrationBuilder.AddColumn<string>(
            name: "thumbnail_fault",
            table: "recording",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_recording_awaiting_thumbnail",
            table: "recording",
            column: "stopped_at_actual",
            filter: "recording_outcome IS NOT NULL AND thumbnail_state = 'Pending'");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_thumbnail",
            table: "recording",
            sql: "thumbnail_state IN ('Pending', 'Ready', 'Failed', 'Skipped')\nAND (recording_outcome IS DISTINCT FROM 'Failed' OR thumbnail_state <> 'Ready')\nAND (thumbnail_state = 'Failed') = (thumbnail_fault IS NOT NULL)\nAND (thumbnail_fault IS NULL OR thumbnail_fault IN ('ProgrammeMissing', 'SourceOutOfReach', 'Refused', 'TimedOut', 'NothingWasWritten'))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_recording_awaiting_thumbnail",
            table: "recording");

        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_thumbnail",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "thumbnail_fault",
            table: "recording");

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_thumbnail",
            table: "recording",
            sql: "thumbnail_state IN ('Pending', 'Ready', 'Failed', 'Skipped')\nAND (recording_outcome IS DISTINCT FROM 'Failed' OR thumbnail_state <> 'Ready')");
    }
}
