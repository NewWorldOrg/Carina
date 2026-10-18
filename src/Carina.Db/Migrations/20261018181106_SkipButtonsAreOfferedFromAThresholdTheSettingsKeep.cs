using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class SkipButtonsAreOfferedFromAThresholdTheSettingsKeep : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AlterColumn<DateTime>(
            name: "learning_changed_at",
            table: "segment_settings",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTime),
            oldType: "timestamp with time zone");

        migrationBuilder.AddColumn<decimal>(
            name: "skip_threshold",
            table: "segment_settings",
            type: "numeric(3,2)",
            nullable: false,
            defaultValue: 0.7m);

        migrationBuilder.AddColumn<DateTime>(
            name: "skip_threshold_changed_at",
            table: "segment_settings",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_settings_skip_threshold",
            table: "segment_settings",
            sql: "skip_threshold BETWEEN 0.50 AND 0.95");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_settings_skip_threshold",
            table: "segment_settings");

        migrationBuilder.DropColumn(
            name: "skip_threshold",
            table: "segment_settings");

        migrationBuilder.DropColumn(
            name: "skip_threshold_changed_at",
            table: "segment_settings");

        migrationBuilder.Sql("DELETE FROM segment_settings WHERE learning_changed_at IS NULL");

        migrationBuilder.AlterColumn<DateTime>(
            name: "learning_changed_at",
            table: "segment_settings",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTime),
            oldType: "timestamp with time zone",
            oldNullable: true);
    }
}
