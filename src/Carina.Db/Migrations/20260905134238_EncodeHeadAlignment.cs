using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Where the artefact's clock stands against the source's, kept on the job: the source's start
/// and the head skipped, which together are the shift a caption takes, and the two lengths that
/// say whether the clocks still agree at the end. A head skip beyond five seconds cannot be written.
/// </summary>
public partial class EncodeHeadAlignment : Migration
{
    /// <inheritdoc />
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

    /// <inheritdoc />
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
