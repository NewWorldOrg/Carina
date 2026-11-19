using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class VersionsBuiltInAreMarkedAndThoseNeverUsedLetGo : Migration
{
    private const string TakeTurn = "SELECT pg_advisory_xact_lock(5243197610103)";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<bool>(
            name: "built_in",
            table: "segment_model_version",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql(TakeTurn);
        migrationBuilder.Sql("UPDATE segment_model_version SET built_in = true WHERE evidence IS NULL");
        migrationBuilder.Sql(
            """
            DELETE FROM segment_model_version AS version
            WHERE version.built_in
              AND version.adopted_at IS NULL
              AND NOT EXISTS (SELECT 1 FROM segment_detection WHERE model_version = version.number)
              AND NOT EXISTS (SELECT 1 FROM encode_job WHERE chapters_model_version = version.number)
              AND NOT EXISTS (SELECT 1 FROM segment_skip_event WHERE model_version = version.number)
              AND NOT EXISTS (SELECT 1 FROM segment_learning_round WHERE candidate = version.number OR in_use_version = version.number)
            """);

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_model_version_built_in",
            table: "segment_model_version",
            sql: "NOT built_in OR evidence IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_model_version_built_in",
            table: "segment_model_version");

        migrationBuilder.DropColumn(
            name: "built_in",
            table: "segment_model_version");
    }
}
