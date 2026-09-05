using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class EncodeHeadAlignment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job");

        migrationBuilder.AddColumn<TimeSpan>(
            name: "artefact_length",
            table: "encode_job",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<TimeSpan>(
            name: "head_skip",
            table: "encode_job",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<TimeSpan>(
            name: "source_length",
            table: "encode_job",
            type: "interval",
            nullable: true);

        migrationBuilder.AddColumn<TimeSpan>(
            name: "source_start",
            table: "encode_job",
            type: "interval",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_alignment",
            table: "encode_job",
            sql: "((head_skip IS NULL) = (source_start IS NULL))\nAND (head_skip IS NULL OR status <> 'Queued')\nAND (head_skip IS NULL OR head_skip BETWEEN interval '0' AND interval '5 seconds')\nAND (source_start IS NULL OR source_start >= interval '0')\nAND (source_length IS NULL OR (head_skip IS NOT NULL AND source_length > interval '0'))\nAND (artefact_length IS NULL OR (head_skip IS NOT NULL AND artefact_length >= interval '0'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job",
            sql: "((status = 'Failed') = (failure IS NOT NULL))\nAND ((failure IS NULL) = (failure_note IS NULL))\nAND ((failure IS NULL) = (failure_noticed_at IS NULL))\nAND (failure IS NULL OR failure IN ('FfmpegExitedNonZero', 'NotEnoughRoom', 'SourceMissing', 'CapabilityUnavailable', 'TimedOut', 'DestinationCollision', 'HeadTooFar'))");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_alignment",
            table: "encode_job");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "artefact_length",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "head_skip",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "source_length",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "source_start",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job",
            sql: "((status = 'Failed') = (failure IS NOT NULL))\nAND ((failure IS NULL) = (failure_note IS NULL))\nAND ((failure IS NULL) = (failure_noticed_at IS NULL))\nAND (failure IS NULL OR failure IN ('FfmpegExitedNonZero', 'NotEnoughRoom', 'SourceMissing', 'CapabilityUnavailable', 'TimedOut', 'DestinationCollision'))");
    }
}
