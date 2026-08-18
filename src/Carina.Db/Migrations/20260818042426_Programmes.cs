using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class Programmes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "programme",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: false),
                start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                is_shadow = table.Column<bool>(type: "boolean", nullable: false),
                genres = table.Column<string>(type: "jsonb", nullable: false),
                items = table.Column<string>(type: "jsonb", nullable: false),
                related = table.Column<string>(type: "jsonb", nullable: false),
                has_subtitles = table.Column<bool>(type: "boolean", nullable: false),
                source = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_programme", x => new { x.network_id, x.service_id, x.event_id });
                table.CheckConstraint("ck_programme_runs_forward", "end_at IS NULL OR end_at > start_at");
                table.CheckConstraint("ck_programme_source", "source IN ('PresentFollowing', 'ScheduleBasic', 'ScheduleExtended')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_programme_start_at",
            table: "programme",
            column: "start_at");

        migrationBuilder.CreateIndex(
            name: "ix_programme_updated_at",
            table: "programme",
            column: "updated_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "programme");
    }
}
