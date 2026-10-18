using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheAnswersAndTheSkipsAreKeptWithoutAKeyIntoTheRecording : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "segment_answer",
            columns: table => new
            {
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                segments = table.Column<string>(type: "jsonb", nullable: false),
                length = table.Column<TimeSpan>(type: "interval", nullable: false),
                confirmed = table.Column<bool>(type: "boolean", nullable: false),
                confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                answers = table.Column<int>(type: "integer", nullable: false),
                withdrawn_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_answer", x => x.recording_id);
                table.CheckConstraint("ck_segment_answer_answers", "answers >= 1");
                table.CheckConstraint("ck_segment_answer_confirmed", "confirmed = (confirmed_at IS NOT NULL)");
                table.CheckConstraint("ck_segment_answer_segments", "jsonb_typeof(segments) = 'array' AND jsonb_array_length(segments) >= 1");
            });

        migrationBuilder.CreateTable(
            name: "segment_skip_event",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                happened_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                @event = table.Column<string>(name: "event", type: "character varying(32)", maxLength: 32, nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                from_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                to_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                model_version = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_skip_event", x => x.id);
                table.CheckConstraint("ck_segment_skip_event_event", "event IN ('Skipped', 'Dismissed', 'Returned')");
                table.CheckConstraint("ck_segment_skip_event_kind", "kind IN ('Break', 'Opening', 'Ending')");
                table.CheckConstraint("ck_segment_skip_event_moments", "from_at >= interval '0' AND to_at >= interval '0'");
                table.ForeignKey(
                    name: "fk_segment_skip_event_segment_model_version_model_version",
                    column: x => x.model_version,
                    principalTable: "segment_model_version",
                    principalColumn: "number",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "ix_segment_answer_confirmed",
            table: "segment_answer",
            column: "confirmed_at",
            filter: "confirmed AND withdrawn_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_segment_skip_event_happened_at",
            table: "segment_skip_event",
            column: "happened_at");

        migrationBuilder.CreateIndex(
            name: "ix_segment_skip_event_model_version",
            table: "segment_skip_event",
            column: "model_version");

        migrationBuilder.CreateIndex(
            name: "ix_segment_skip_event_recording_id_happened_at",
            table: "segment_skip_event",
            columns: ["recording_id", "happened_at"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "segment_answer");

        migrationBuilder.DropTable(
            name: "segment_skip_event");
    }
}
