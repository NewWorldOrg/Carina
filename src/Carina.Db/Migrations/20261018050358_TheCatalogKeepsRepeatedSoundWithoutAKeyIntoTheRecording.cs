using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheCatalogKeepsRepeatedSoundWithoutAKeyIntoTheRecording : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name: "matched_at",
            table: "segment_extraction",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "segment_catalog_entry",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                confidence = table.Column<double>(type: "double precision", nullable: true),
                decided_by = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                length = table.Column<TimeSpan>(type: "interval", nullable: false),
                fingerprint = table.Column<byte[]>(type: "bytea", nullable: false),
                occurrences = table.Column<int>(type: "integer", nullable: false),
                series_count = table.Column<int>(type: "integer", nullable: false),
                station_count = table.Column<int>(type: "integer", nullable: false),
                first_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                last_seen_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_catalog_entry", x => x.id);
                table.CheckConstraint("ck_segment_catalog_entry_confidence", "(confidence IS NULL) = (kind = 'Unknown') AND (confidence IS NULL OR confidence BETWEEN 0 AND 1)");
                table.CheckConstraint("ck_segment_catalog_entry_counts", "occurrences >= 2 AND series_count >= 1 AND station_count >= 1");
                table.CheckConstraint("ck_segment_catalog_entry_decided_by", "(decided_by IS NULL) = (kind = 'Unknown') AND (decided_by IS NULL OR decided_by IN ('Answer', 'Pattern'))");
                table.CheckConstraint("ck_segment_catalog_entry_fingerprint", "octet_length(fingerprint) BETWEEN 4 AND 45000 AND octet_length(fingerprint) % 4 = 0");
                table.CheckConstraint("ck_segment_catalog_entry_kind", "kind IN ('Cm', 'Opening', 'Ending', 'ProgrammeFixture', 'StationFixture', 'Unknown')");
                table.CheckConstraint("ck_segment_catalog_entry_length", "length > interval '0'");
                table.CheckConstraint("ck_segment_catalog_entry_seen", "last_seen_at >= first_seen_at");
            });

        migrationBuilder.CreateTable(
            name: "segment_catalog_occurrence",
            columns: table => new
            {
                id = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                entry_id = table.Column<Guid>(type: "uuid", nullable: false),
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                starts_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                ends_at = table.Column<TimeSpan>(type: "interval", nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                series_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                recording_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                bit_error_rate = table.Column<float>(type: "real", nullable: false),
                kind_override = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                matched_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_catalog_occurrence", x => x.id);
                table.CheckConstraint("ck_segment_catalog_occurrence_bit_error_rate", "bit_error_rate BETWEEN 0 AND 0.3::real");
                table.CheckConstraint("ck_segment_catalog_occurrence_kind_override", "kind_override IS NULL OR kind_override IN ('Programme', 'Break', 'Opening', 'Ending')");
                table.CheckConstraint("ck_segment_catalog_occurrence_span", "starts_at >= interval '0' AND ends_at > starts_at");
                table.ForeignKey(
                    name: "fk_segment_catalog_occurrence_segment_catalog_entry_entry_id",
                    column: x => x.entry_id,
                    principalTable: "segment_catalog_entry",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_extraction_matched",
            table: "segment_extraction",
            sql: "matched_at IS NULL OR matched_at >= created_at");

        migrationBuilder.CreateIndex(
            name: "ix_segment_catalog_entry_kind",
            table: "segment_catalog_entry",
            column: "kind");

        migrationBuilder.CreateIndex(
            name: "ix_segment_catalog_entry_last_seen_at",
            table: "segment_catalog_entry",
            column: "last_seen_at");

        migrationBuilder.CreateIndex(
            name: "ix_segment_catalog_occurrence_recording_id",
            table: "segment_catalog_occurrence",
            column: "recording_id");

        migrationBuilder.CreateIndex(
            name: "ix_segment_catalog_occurrence_recording_started_at",
            table: "segment_catalog_occurrence",
            column: "recording_started_at");

        migrationBuilder.CreateIndex(
            name: "ux_segment_catalog_occurrence_place",
            table: "segment_catalog_occurrence",
            columns: new[] { "entry_id", "recording_id", "starts_at" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "segment_catalog_occurrence");

        migrationBuilder.DropTable(
            name: "segment_catalog_entry");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_extraction_matched",
            table: "segment_extraction");

        migrationBuilder.DropColumn(
            name: "matched_at",
            table: "segment_extraction");
    }
}
