using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ProgrammesKeepTheirRunningStatus : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "running",
            table: "programme",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Undetermined");

        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN running DROP DEFAULT");

        migrationBuilder.AddCheckConstraint(
            name: "ck_programme_running",
            table: "programme",
            sql: "running IN ('Undetermined', 'NotRunning', 'StartsInSeconds', 'Pausing', 'Running', 'OffAir')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_programme_running",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "running",
            table: "programme");
    }
}
