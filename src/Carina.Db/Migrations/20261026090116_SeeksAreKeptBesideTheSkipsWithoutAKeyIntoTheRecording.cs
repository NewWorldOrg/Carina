using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class SeeksAreKeptBesideTheSkipsWithoutAKeyIntoTheRecording : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "segment_seek_event",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                happened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                @event = table.Column<string>(name: "event", type: "character varying(32)", maxLength: 32, nullable: false),
                means = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                from_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                to_at = table.Column<TimeSpan>(type: "interval", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_seek_event", x => x.id);
                table.CheckConstraint("ck_segment_seek_event_event", "event IN ('Forward', 'Returned')");
                table.CheckConstraint("ck_segment_seek_event_means", "means IN ('Bar', 'Step', 'Chapter', 'Key')");
                table.CheckConstraint("ck_segment_seek_event_moments", "from_at >= interval '0' AND to_at >= interval '0'");
                table.CheckConstraint("ck_segment_seek_event_way", "(event = 'Forward') = (to_at > from_at) AND to_at <> from_at");
            });

        migrationBuilder.CreateIndex(
            name: "ix_segment_seek_event_happened_at",
            table: "segment_seek_event",
            column: "happened_at");

        migrationBuilder.CreateIndex(
            name: "ix_segment_seek_event_recording_id_happened_at",
            table: "segment_seek_event",
            columns: ["recording_id", "happened_at"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "segment_seek_event");
    }
}
