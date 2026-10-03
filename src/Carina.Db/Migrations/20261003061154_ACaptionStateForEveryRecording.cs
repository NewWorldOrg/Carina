using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ACaptionStateForEveryRecording : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "caption_attempts",
            table: "recording",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<int>(
            name: "caption_pictures",
            table: "recording",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "caption_state",
            table: "recording",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Pending");

        migrationBuilder.AddColumn<DateTime>(
            name: "captions_made_at",
            table: "recording",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_recording_captions",
            table: "recording",
            sql: "caption_state IN ('Pending', 'Ready', 'Absent', 'Failed')\nAND (caption_state = 'Pending' OR recording_outcome IS NOT NULL)\nAND (caption_state = 'Pending') = (captions_made_at IS NULL)\nAND (caption_state = 'Ready') = (caption_pictures IS NOT NULL)\nAND (caption_pictures IS NULL OR caption_pictures > 0)\nAND (caption_state = 'Failed') = (caption_attempts > 0)\nAND caption_attempts >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_recording_captions",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "caption_attempts",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "caption_pictures",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "caption_state",
            table: "recording");

        migrationBuilder.DropColumn(
            name: "captions_made_at",
            table: "recording");
    }
}
