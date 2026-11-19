using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class TheChannelASignalSampleWasTakenOn : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "physical_channel",
            table: "quality_signal_sample",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "physical_channel",
            table: "quality_signal_rollup",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_signal_sample_physical_channel",
            table: "quality_signal_sample",
            sql: "physical_channel IS NULL OR physical_channel > 0");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_signal_rollup_physical_channel",
            table: "quality_signal_rollup",
            sql: "physical_channel IS NULL OR physical_channel > 0");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_signal_sample_physical_channel",
            table: "quality_signal_sample");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_signal_rollup_physical_channel",
            table: "quality_signal_rollup");

        migrationBuilder.DropColumn(
            name: "physical_channel",
            table: "quality_signal_sample");

        migrationBuilder.DropColumn(
            name: "physical_channel",
            table: "quality_signal_rollup");
    }
}
