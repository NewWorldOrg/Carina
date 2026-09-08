using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class SignalSamplesThatCouldNotBeTaken : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "not_taken_because",
            table: "quality_signal_sample",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_signal_sample_not_taken",
            table: "quality_signal_sample",
            sql: "(not_taken_because IS NULL OR not_taken_because IN ('DriverUnreachable', 'NothingReported', 'NoTimeGiven', 'FiguresRefused'))\nAND (\n    not_taken_because IS NULL\n    OR (\n        NOT locked\n        AND cnr_milli_decibels IS NULL\n        AND bit_errors = '[]'::jsonb\n        AND metrics_not_read = '[]'::jsonb\n    )\n)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_signal_sample_not_taken",
            table: "quality_signal_sample");

        migrationBuilder.DropColumn(
            name: "not_taken_because",
            table: "quality_signal_sample");
    }
}
