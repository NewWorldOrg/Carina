using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ThePictureABroadcastAnnounces : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "video",
            table: "programme",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Undetermined");

        migrationBuilder.AddColumn<string>(
            name: "aspect",
            table: "programme",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Undetermined");

        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN video DROP DEFAULT");
        migrationBuilder.Sql("ALTER TABLE programme ALTER COLUMN aspect DROP DEFAULT");

        migrationBuilder.AddCheckConstraint(
            name: "ck_programme_video",
            table: "programme",
            sql: "video IN ('Undetermined', 'Progressive180', 'Progressive240', 'Interlaced480',"
                + " 'Progressive480', 'Progressive720', 'Interlaced1080', 'Progressive1080',"
                + " 'Progressive2160', 'Progressive4320')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_programme_aspect",
            table: "programme",
            sql: "aspect IN ('Undetermined', 'FourByThree', 'SixteenByNineWithPanVector',"
                + " 'SixteenByNine', 'WiderThanSixteenByNine')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_programme_aspect",
            table: "programme");

        migrationBuilder.DropCheckConstraint(
            name: "ck_programme_video",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "aspect",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "video",
            table: "programme");
    }
}
