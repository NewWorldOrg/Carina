using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class HowManySoundsAProgrammeAnnounces : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<int>(
            name: "sounds",
            table: "programme",
            type: "integer",
            nullable: false,
            defaultValue: 0);

        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN sounds DROP DEFAULT");

        migrationBuilder.AddCheckConstraint(
            name: "ck_programme_sounds",
            table: "programme",
            sql: "sounds >= 0");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_programme_sounds",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "sounds",
            table: "programme");
    }
}
