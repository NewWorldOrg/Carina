using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class LogoSweepVisits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.CreateTable(
            name: "logo_visit",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: false),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                last_attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_collected_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_logo_visit", x => new { x.network_id, x.transport_stream_id });
                table.CheckConstraint("ck_logo_visit_collected", "(outcome <> 'Collected') OR (last_collected_at IS NOT NULL)");
                table.CheckConstraint("ck_logo_visit_outcome", "outcome IN ('Collected', 'NothingArrived', 'NoLock', 'Interrupted')");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "logo_visit");
    }
}
