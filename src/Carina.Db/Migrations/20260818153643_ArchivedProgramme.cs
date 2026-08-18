using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ArchivedProgramme : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "archived_programme",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                end_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                has_subtitles = table.Column<bool>(type: "boolean", nullable: false),
                genres = table.Column<string>(type: "jsonb", nullable: false),
                items = table.Column<string>(type: "jsonb", nullable: false),
                archived_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_archived_programme", x => new { x.network_id, x.service_id, x.event_id, x.start_at });
                table.CheckConstraint("ck_archived_programme_runs_forward", "end_at > start_at");
            });

        migrationBuilder.CreateIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme",
            column: "end_at");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "archived_programme");
    }
}
