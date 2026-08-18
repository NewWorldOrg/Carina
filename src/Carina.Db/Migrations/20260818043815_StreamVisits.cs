using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class StreamVisits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "stream_visit",
            columns: table => new
            {
                network_id = table.Column<int>(type: "integer", nullable: false),
                transport_stream_id = table.Column<int>(type: "integer", nullable: false),
                last_attempted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_completed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                consecutive_incomplete = table.Column<int>(type: "integer", nullable: false),
                last_duration_milliseconds = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_stream_visit", x => new { x.network_id, x.transport_stream_id });
                table.CheckConstraint("ck_stream_visit_completion", "last_completed_at IS NULL OR last_completed_at <= last_attempted_at");
                table.CheckConstraint("ck_stream_visit_counts", "consecutive_incomplete >= 0 AND last_duration_milliseconds >= 0");
                table.CheckConstraint("ck_stream_visit_outcome", "outcome IN ('Complete', 'BasicOnly', 'Incomplete', 'Interrupted', 'NoLock', 'NoBytes')");
            });

        migrationBuilder.CreateIndex(
            name: "ix_stream_visit_last_completed_at",
            table: "stream_visit",
            column: "last_completed_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "stream_visit");
    }
}
