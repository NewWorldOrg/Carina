using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class StationLogos : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.AddColumn<string>(
            name: "logo_declaration",
            table: "broadcast_service",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "NotYetRead");

        migrationBuilder.Sql("ALTER TABLE broadcast_service ALTER COLUMN logo_declaration DROP DEFAULT");

        migrationBuilder.AddColumn<int>(
            name: "logo_id",
            table: "broadcast_service",
            type: "integer",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "station_logo",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                logo_id = table.Column<int>(type: "integer", nullable: false),
                logo_type = table.Column<int>(type: "integer", nullable: false),
                logo_version = table.Column<int>(type: "integer", nullable: false),
                width = table.Column<int>(type: "integer", nullable: false),
                height = table.Column<int>(type: "integer", nullable: false),
                picture = table.Column<byte[]>(type: "bytea", nullable: false),
                collected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_station_logo", x => new { x.network_id, x.logo_id });
                table.CheckConstraint("ck_station_logo_carries_a_picture", "octet_length(picture) BETWEEN 1 AND 262144");
                table.CheckConstraint("ck_station_logo_id", "logo_id BETWEEN 0 AND 511");
                table.CheckConstraint("ck_station_logo_measures_something", "width BETWEEN 1 AND 4096 AND height BETWEEN 1 AND 4096");
            });

        migrationBuilder.CreateIndex(
            name: "ix_broadcast_service_network_id_logo_id",
            table: "broadcast_service",
            columns: new[] { "network_id", "logo_id" });

        migrationBuilder.AddCheckConstraint(
            name: "ck_broadcast_service_logo",
            table: "broadcast_service",
            sql: "logo_declaration IN ('NotYetRead', 'InTheCommonDataTable', 'NoPictureIsBroadcast') AND (logo_id IS NOT NULL) = (logo_declaration = 'InTheCommonDataTable')");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "station_logo");

        migrationBuilder.DropIndex(
            name: "ix_broadcast_service_network_id_logo_id",
            table: "broadcast_service");

        migrationBuilder.DropCheckConstraint(
            name: "ck_broadcast_service_logo",
            table: "broadcast_service");

        migrationBuilder.DropColumn(
            name: "logo_declaration",
            table: "broadcast_service");

        migrationBuilder.DropColumn(
            name: "logo_id",
            table: "broadcast_service");
    }
}
