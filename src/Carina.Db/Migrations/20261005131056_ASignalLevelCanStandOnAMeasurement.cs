using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ASignalLevelCanStandOnAMeasurement : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "cause",
            table: "quality_threshold_change",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Hand");

        migrationBuilder.AddColumn<bool>(
            name: "by_hand",
            table: "quality_threshold",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("UPDATE quality_threshold SET by_hand = true, provisional = true;");

        migrationBuilder.AddColumn<DateTime>(
            name: "measured_at",
            table: "quality_threshold",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "measured_from",
            table: "quality_threshold",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "measured_sessions",
            table: "quality_threshold",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "measured_sessions_dropped",
            table: "quality_threshold",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "measured_until",
            table: "quality_threshold",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "measured_value",
            table: "quality_threshold",
            type: "double precision",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_change_cause",
            table: "quality_threshold_change",
            sql: "cause IN ('Hand', 'Measurement')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_measurement",
            table: "quality_threshold",
            sql: "(measured_value IS NULL) = (measured_sessions IS NULL)\nAND (measured_value IS NULL) = (measured_sessions_dropped IS NULL)\nAND (measured_value IS NULL) = (measured_from IS NULL)\nAND (measured_value IS NULL) = (measured_until IS NULL)\nAND (measured_value IS NULL) = (measured_at IS NULL)\nAND (measured_sessions IS NULL OR measured_sessions > 0)\nAND (measured_sessions_dropped IS NULL\n    OR (measured_sessions_dropped >= 0 AND measured_sessions_dropped <= measured_sessions))\nAND (measured_from IS NULL OR measured_from <= measured_until)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_quality_threshold_source",
            table: "quality_threshold",
            sql: "(NOT by_hand OR provisional)\nAND (provisional OR current_value = measured_value)");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_change_cause",
            table: "quality_threshold_change");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_measurement",
            table: "quality_threshold");

        migrationBuilder.DropCheckConstraint(
            name: "ck_quality_threshold_source",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "cause",
            table: "quality_threshold_change");

        migrationBuilder.DropColumn(
            name: "by_hand",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "measured_at",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "measured_from",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "measured_sessions",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "measured_sessions_dropped",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "measured_until",
            table: "quality_threshold");

        migrationBuilder.DropColumn(
            name: "measured_value",
            table: "quality_threshold");
    }
}
