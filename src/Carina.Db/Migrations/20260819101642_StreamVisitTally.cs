using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class StreamVisitTally : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stream_visit_tally",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                table_id = table.Column<int>(type: "integer", nullable: false),
                last_table_id = table.Column<int>(type: "integer", nullable: false),
                segments_declared = table.Column<int>(type: "integer", nullable: false),
                segments_heard = table.Column<int>(type: "integer", nullable: false),
                sections_declared = table.Column<int>(type: "integer", nullable: false),
                sections_heard = table.Column<int>(type: "integer", nullable: false),
                version_changes = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stream_visit_tally", x => new { x.network_id, x.transport_stream_id, x.service_id, x.table_id });
                table.CheckConstraint("ck_stream_visit_tally_counts", "segments_declared >= segments_heard AND segments_heard >= 0 AND sections_declared >= 0 AND sections_heard >= 0 AND version_changes >= 0");
                table.ForeignKey(
                    name: "fk_stream_visit_tally_stream_visit_network_id_transport_stream",
                    columns: x => new { x.network_id, x.transport_stream_id },
                    principalTable: "stream_visit",
                    principalColumns: new[] { "network_id", "transport_stream_id" },
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "stream_visit_tally");
    }
}
