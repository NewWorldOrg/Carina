using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class OnlyAVisitThatHeardNothingWaitsLonger : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int>(
            name: "consecutive_unheard",
            table: "stream_visit",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.AddColumn<bool>(
            name: "reached_the_goal",
            table: "stream_visit",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("ALTER TABLE stream_visit ALTER COLUMN consecutive_unheard DROP DEFAULT");
        migrationBuilder.Sql("ALTER TABLE stream_visit ALTER COLUMN reached_the_goal DROP DEFAULT");

        migrationBuilder.AddCheckConstraint(
            name: "ck_stream_visit_unheard",
            table: "stream_visit",
            sql: "consecutive_unheard >= 0 AND consecutive_unheard <= consecutive_incomplete");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_stream_visit_unheard",
            table: "stream_visit");

        migrationBuilder.DropColumn(
            name: "reached_the_goal",
            table: "stream_visit");

        migrationBuilder.DropColumn(
            name: "consecutive_unheard",
            table: "stream_visit");
    }
}
