using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class ProgrammeBroadcastDayOfWeek : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "broadcast_dow",
            table: "programme",
            type: "integer",
            nullable: false,
            computedColumnSql: "extract(dow from ((start_at AT TIME ZONE INTERVAL '+09:00') - INTERVAL '4 hours'))::integer",
            stored: true);

        migrationBuilder.AddColumn<int>(
            name: "broadcast_dow",
            table: "archived_programme",
            type: "integer",
            nullable: false,
            computedColumnSql: "extract(dow from ((start_at AT TIME ZONE INTERVAL '+09:00') - INTERVAL '4 hours'))::integer",
            stored: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "broadcast_dow",
            table: "programme");

        migrationBuilder.DropColumn(
            name: "broadcast_dow",
            table: "archived_programme");
    }
}
