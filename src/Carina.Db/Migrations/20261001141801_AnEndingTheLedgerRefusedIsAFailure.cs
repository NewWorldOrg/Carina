using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AnEndingTheLedgerRefusedIsAFailure : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job",
            sql: "((status = 'Failed') = (failure IS NOT NULL))\nAND ((failure IS NULL) = (failure_note IS NULL))\nAND ((failure IS NULL) = (failure_noticed_at IS NULL))\nAND (failure IS NULL OR failure IN ('FfmpegExitedNonZero', 'NotEnoughRoom', 'SourceMissing', 'CapabilityUnavailable', 'TimedOut', 'DestinationCollision', 'HeadTooFar', 'EndingNotKept'))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_failure",
            table: "encode_job",
            sql: "((status = 'Failed') = (failure IS NOT NULL))\nAND ((failure IS NULL) = (failure_note IS NULL))\nAND ((failure IS NULL) = (failure_noticed_at IS NULL))\nAND (failure IS NULL OR failure IN ('FfmpegExitedNonZero', 'NotEnoughRoom', 'SourceMissing', 'CapabilityUnavailable', 'TimedOut', 'DestinationCollision', 'HeadTooFar'))");
    }
}
