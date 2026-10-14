using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheGuideReadsEachServiceInStartOrder : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "ix_programme_network_id_service_id_start_at_event_id",
            table: "programme",
            columns: new[] { "network_id", "service_id", "start_at", "event_id" },
            filter: "NOT is_shadow")
            .Annotation("Npgsql:IndexInclude", new[] { "end_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_programme_network_id_service_id_start_at_event_id",
            table: "programme");
    }
}
