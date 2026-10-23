using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class AJobMakesAnArtefactLargerThanItsRecordingAgain : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "remade",
            table: "encode_job",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_remade",
            table: "encode_job",
            sql: "remade IS NULL OR (remade IN ('LargerThanTheSource', 'StillLargerThanTheSource', 'NoSmallerTheSecondTime') AND status <> 'Queued')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_remade",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "remade",
            table: "encode_job");
    }
}
