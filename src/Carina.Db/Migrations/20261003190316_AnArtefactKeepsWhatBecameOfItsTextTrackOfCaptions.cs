using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AnArtefactKeepsWhatBecameOfItsTextTrackOfCaptions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file");

        migrationBuilder.AddColumn<string>(
            name: "caption_track",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "caption_track_attempts",
            table: "encode_job",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<DateTime>(
            name: "caption_track_from",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file",
            sql: "kind IN ('WorkFile', 'Chapters', 'ReplacedArtefact', 'Captions', 'CaptionedWork')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_caption_track",
            table: "encode_job",
            sql: "(caption_track IS NULL) = (caption_track_from IS NULL)\nAND (caption_track IS NULL OR caption_track IN ('Added', 'Withheld', 'Failed'))\nAND caption_track_attempts >= 0\nAND (caption_track_attempts = 0 OR caption_track = 'Failed')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_caption_track",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "caption_track",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "caption_track_attempts",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "caption_track_from",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file",
            sql: "kind IN ('WorkFile', 'Chapters', 'ReplacedArtefact')");
    }
}
