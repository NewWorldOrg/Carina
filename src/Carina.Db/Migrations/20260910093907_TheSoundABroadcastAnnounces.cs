using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheSoundABroadcastAnnounces : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "audio",
            table: "programme",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Undetermined");

        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN audio DROP DEFAULT");

        migrationBuilder.AddCheckConstraint(
            name: "ck_programme_audio",
            table: "programme",
            sql: "audio IN ('Undetermined', 'Mono', 'Stereo', 'DualMono', 'Surround')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_programme_audio",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "audio",
            table: "programme");
    }
}
