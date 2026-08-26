using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ServiceReach : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "service_reach_config",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                hours_of_silence = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_service_reach_config", x => x.id);
                table.CheckConstraint("ck_service_reach_config_hours_of_silence", "hours_of_silence >= 1 AND hours_of_silence <= 720");
                table.CheckConstraint("ck_service_reach_config_single_row", "id = 1");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "service_reach_config");
    }
}
