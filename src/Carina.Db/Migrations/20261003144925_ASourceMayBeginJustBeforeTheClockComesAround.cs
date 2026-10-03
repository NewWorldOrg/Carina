using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ASourceMayBeginJustBeforeTheClockComesAround : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_alignment",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_alignment",
            table: "encode_job",
            sql: "((head_skip IS NULL) = (source_start IS NULL))\nAND (head_skip IS NULL OR status <> 'Queued')\nAND (head_skip IS NULL OR head_skip BETWEEN interval '0' AND interval '5 seconds')\nAND (source_start IS NULL OR source_start > interval '-95443717689 microseconds')\nAND (source_length IS NULL OR (head_skip IS NOT NULL AND source_length > interval '0'))\nAND (artefact_length IS NULL OR (head_skip IS NOT NULL AND artefact_length >= interval '0'))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_alignment",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_alignment",
            table: "encode_job",
            sql: "((head_skip IS NULL) = (source_start IS NULL))\nAND (head_skip IS NULL OR status <> 'Queued')\nAND (head_skip IS NULL OR head_skip BETWEEN interval '0' AND interval '5 seconds')\nAND (source_start IS NULL OR source_start >= interval '0')\nAND (source_length IS NULL OR (head_skip IS NOT NULL AND source_length > interval '0'))\nAND (artefact_length IS NULL OR (head_skip IS NOT NULL AND artefact_length >= interval '0'))");
    }
}
