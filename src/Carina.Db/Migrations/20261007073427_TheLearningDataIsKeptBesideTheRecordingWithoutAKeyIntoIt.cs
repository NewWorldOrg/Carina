using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <inheritdoc />
public partial class TheLearningDataIsKeptBesideTheRecordingWithoutAKeyIntoIt : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "segment_extraction",
            columns: table => new
            {
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                read_through = table.Column<TimeSpan>(type: "interval", nullable: false),
                gaps = table.Column<string>(type: "jsonb", nullable: false),
                failures = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                failure = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                audio = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                episode = table.Column<int>(type: "integer", nullable: true),
                genres = table.Column<string>(type: "jsonb", nullable: false),
                marks = table.Column<string>(type: "jsonb", nullable: false),
                name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                network_id = table.Column<int>(type: "integer", nullable: false),
                programme_ends_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                programme_starts_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                recording_started_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                series_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                service_id = table.Column<int>(type: "integer", nullable: false),
                sound_offset = table.Column<TimeSpan>(type: "interval", nullable: true),
                sound_stream = table.Column<int>(type: "integer", nullable: true),
                version_number = table.Column<int>(type: "integer", nullable: true),
                version_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_extraction", x => x.recording_id);
                table.CheckConstraint("ck_segment_extraction_failure", "failures >= 0\nAND (failure IS NULL) = (failure_reason IS NULL)\nAND (failure IS NULL) = (failures = 0)\nAND (failure IS NULL OR failure IN ('FfmpegMissing', 'StreamMissing', 'TimingMismatch', 'Other'))\nAND (state <> 'Failed' OR failure IS NOT NULL)\nAND (state NOT IN ('Done', 'Partial') OR failures = 0)");
                table.CheckConstraint("ck_segment_extraction_programme", "(programme_ends_at IS NULL OR programme_ends_at > programme_starts_at)\nAND (episode IS NULL OR episode >= 0)\nAND audio IN ('Undetermined', 'Mono', 'Stereo', 'DualMono', 'Surround')\nAND jsonb_typeof(genres) = 'array'\nAND jsonb_typeof(marks) = 'array'");
                table.CheckConstraint("ck_segment_extraction_progress", "read_through >= interval '0'\nAND jsonb_typeof(gaps) = 'array'\nAND (sound_stream IS NULL) = (sound_offset IS NULL)\nAND (sound_stream IS NULL OR sound_stream >= 0)");
                table.CheckConstraint("ck_segment_extraction_state", "state IN ('Following', 'Waiting', 'Reading', 'Done', 'Partial', 'Failed')");
                table.CheckConstraint("ck_segment_extraction_times", "updated_at >= created_at");
                table.CheckConstraint("ck_segment_extraction_version", "(version_number IS NULL) = (version_origin IS NULL)\nAND (version_number IS NULL OR version_number >= 1)\nAND (version_origin IS NULL OR version_origin IN ('RecordingFile', 'ReducedCopy'))\nAND (version_number IS NOT NULL OR state = 'Waiting')");
            });

        migrationBuilder.CreateTable(
            name: "segment_learning_data",
            columns: table => new
            {
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<short>(type: "smallint", nullable: false),
                chunk = table.Column<int>(type: "integer", nullable: false),
                bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                written_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                version_number = table.Column<int>(type: "integer", nullable: false),
                version_origin = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_learning_data", x => new { x.recording_id, x.kind, x.chunk });
                table.CheckConstraint("ck_segment_learning_data_bytes", "octet_length(bytes) > 0");
                table.CheckConstraint("ck_segment_learning_data_chunk", "chunk BETWEEN 0 AND 3579139");
                table.CheckConstraint("ck_segment_learning_data_kind", "kind IN (1, 2, 3, 4, 5)");
                table.CheckConstraint("ck_segment_learning_data_version", "version_number >= 1 AND version_origin IN ('RecordingFile', 'ReducedCopy')");
            });

        migrationBuilder.CreateIndex(
            name: "ux_segment_extraction_reading",
            table: "segment_extraction",
            column: "state",
            unique: true,
            filter: "state = 'Reading'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "segment_extraction");

        migrationBuilder.DropTable(
            name: "segment_learning_data");
    }
}
