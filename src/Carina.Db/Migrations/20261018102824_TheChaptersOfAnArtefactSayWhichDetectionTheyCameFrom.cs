using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheChaptersOfAnArtefactSayWhichDetectionTheyCameFrom : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job");

        migrationBuilder.AddColumn<DateTime>(
            name: "chapters_answered_at",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "chapters_model_version",
            table: "encode_job",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file",
            sql: "kind IN ('WorkFile', 'Chapters', 'ReplacedArtefact', 'Captions', 'CaptionedWork', 'ChapteredWork')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job",
            sql: "((chapters_verdict IS NULL) = (chapters_decided_at IS NULL))\n"
                + "AND ((chapters_verdict IS NULL) = (chapters_detector IS NULL))\n"
                + "AND ((chapters_verdict IS NULL) = (chapters_break_share IS NULL))\n"
                + "AND (chapters_verdict IS NULL OR status <> 'Queued')\n"
                + "AND (chapters_verdict IS NULL OR chapters_verdict IN ('NotAsked', 'Marked', 'NothingFound', 'Discarded', 'Unreadable'))\n"
                + "AND (chapters_detector IS NULL OR chapters_detector IN ('Nobody', 'Ffmpeg', 'Segments'))\n"
                + "AND (chapters_break_share IS NULL OR chapters_break_share BETWEEN 0 AND 1)\n"
                + "AND (chapters_decided_at IS NULL OR chapters_decided_at >= started_at)\n"
                + "AND (chapters_model_version IS NULL OR chapters_model_version >= 1)\n"
                + "AND ((chapters_model_version IS NULL AND chapters_answered_at IS NULL) OR chapters_detector IS NOT DISTINCT FROM 'Segments')\n"
                + "AND (chapters_detector IS DISTINCT FROM 'Segments'\n"
                + "    OR ((chapters_model_version IS NOT NULL OR chapters_answered_at IS NOT NULL) = (chapters_verdict <> 'NotAsked')))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job");

        migrationBuilder.Sql("DELETE FROM encode_scratch_file WHERE kind = 'ChapteredWork'");

        migrationBuilder.Sql(
            """
            UPDATE encode_job
            SET chapters_detector = NULL, chapters_verdict = NULL, chapters_break_share = NULL, chapters_decided_at = NULL
            WHERE chapters_detector = 'Segments'
            """);

        migrationBuilder.DropColumn(
            name: "chapters_answered_at",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "chapters_model_version",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_scratch_file_kind",
            table: "encode_scratch_file",
            sql: "kind IN ('WorkFile', 'Chapters', 'ReplacedArtefact', 'Captions', 'CaptionedWork')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job",
            sql: "((chapters_verdict IS NULL) = (chapters_decided_at IS NULL))\n"
                + "AND ((chapters_verdict IS NULL) = (chapters_detector IS NULL))\n"
                + "AND ((chapters_verdict IS NULL) = (chapters_break_share IS NULL))\n"
                + "AND (chapters_verdict IS NULL OR status <> 'Queued')\n"
                + "AND (chapters_verdict IS NULL OR chapters_verdict IN ('NotAsked', 'Marked', 'NothingFound', 'Discarded', 'Unreadable'))\n"
                + "AND (chapters_detector IS NULL OR chapters_detector IN ('Nobody', 'Ffmpeg'))\n"
                + "AND (chapters_break_share IS NULL OR chapters_break_share BETWEEN 0 AND 1)\n"
                + "AND (chapters_decided_at IS NULL OR chapters_decided_at >= started_at)");
    }
}
