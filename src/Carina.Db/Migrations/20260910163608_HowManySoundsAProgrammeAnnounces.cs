using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class HowManySoundsAProgrammeAnnounces : Migration
{
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
