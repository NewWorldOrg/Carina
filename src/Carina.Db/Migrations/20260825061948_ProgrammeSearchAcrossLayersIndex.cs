using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class ProgrammeSearchAcrossLayersIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme");

        migrationBuilder.CreateIndex(
                name: "ix_archived_programme_end_at",
                table: "archived_programme",
                column: "end_at")
            .Annotation("Npgsql:IndexInclude", new[] { "network_id", "service_id", "event_id", "start_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme");

        migrationBuilder.CreateIndex(
            name: "ix_archived_programme_end_at",
            table: "archived_programme",
            column: "end_at");
    }
}
