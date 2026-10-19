using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

/// <summary>
/// Drops what a person's answer for the stretches of a recording was kept in: the answers, the kind of a catalog
/// entry an answer decided, the threshold an evaluation recommended and when the answer an artefact's chapters came
/// from was given. Every entry heard in a recording that had an answer is put back to be estimated again, an entry an
/// answer decided is not known until it is, and an artefact whose chapters came from an answer forgets where they
/// came from, so that they are compared with the detection again. Going back brings the table and the columns back
/// empty.
/// </summary>
public partial class SegmentsAreNotAnsweredByHand : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            """
            UPDATE segment_catalog_entry
            SET kinds_version = 0
            WHERE id IN (
                SELECT occurrence.entry_id
                FROM segment_catalog_occurrence occurrence
                JOIN segment_answer answer ON answer.recording_id = occurrence.recording_id)
            """);

        migrationBuilder.Sql(
            """
            UPDATE segment_catalog_entry
            SET kind = 'Unknown', confidence = NULL, decided_by = NULL, kinds_version = 0
            WHERE decided_by = 'Answer'
            """);

        migrationBuilder.Sql(
            """
            UPDATE encode_job
            SET chapters_detector = NULL,
                chapters_verdict = NULL,
                chapters_break_share = NULL,
                chapters_decided_at = NULL,
                chapters_model_version = NULL,
                chapters_answered_at = NULL
            WHERE chapters_answered_at IS NOT NULL
            """);

        migrationBuilder.DropTable(
            name: "segment_answer");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_model_version_evaluation",
            table: "segment_model_version");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job");

        migrationBuilder.DropColumn(
            name: "recommended_threshold",
            table: "segment_model_version");

        migrationBuilder.DropColumn(
            name: "chapters_answered_at",
            table: "encode_job");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_model_version_evaluation",
            table: "segment_model_version",
            sql: "(metrics IS NULL OR jsonb_typeof(metrics) = 'object')\nAND (stations IS NULL OR jsonb_typeof(stations) = 'array')\nAND (evidence IS NULL OR jsonb_typeof(evidence) = 'object')\nAND (rollback IS NULL OR jsonb_typeof(rollback) = 'object')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry",
            sql: "(decided_by IS NULL) = (kind = 'Unknown') AND (decided_by IS NULL OR decided_by IN ('Pattern'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job",
            sql: "((chapters_verdict IS NULL) = (chapters_decided_at IS NULL))\nAND ((chapters_verdict IS NULL) = (chapters_detector IS NULL))\nAND ((chapters_verdict IS NULL) = (chapters_break_share IS NULL))\nAND (chapters_verdict IS NULL OR status <> 'Queued')\nAND (chapters_verdict IS NULL OR chapters_verdict IN ('NotAsked', 'Marked', 'NothingFound', 'Discarded', 'Unreadable'))\nAND (chapters_detector IS NULL OR chapters_detector IN ('Nobody', 'Ffmpeg', 'Segments'))\nAND (chapters_break_share IS NULL OR chapters_break_share BETWEEN 0 AND 1)\nAND (chapters_decided_at IS NULL OR chapters_decided_at >= started_at)\nAND (chapters_model_version IS NULL OR chapters_model_version >= 1)\nAND (chapters_model_version IS NULL OR chapters_detector IS NOT DISTINCT FROM 'Segments')\nAND (chapters_detector IS DISTINCT FROM 'Segments'\n    OR ((chapters_model_version IS NOT NULL) = (chapters_verdict <> 'NotAsked')))");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_model_version_evaluation",
            table: "segment_model_version");

        migrationBuilder.DropCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry");

        migrationBuilder.DropCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job");

        migrationBuilder.AddColumn<decimal>(
            name: "recommended_threshold",
            table: "segment_model_version",
            type: "numeric(3,2)",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "chapters_answered_at",
            table: "encode_job",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "segment_answer",
            columns: table => new
            {
                recording_id = table.Column<Guid>(type: "uuid", nullable: false),
                answered_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                answers = table.Column<int>(type: "integer", nullable: false),
                confirmed = table.Column<bool>(type: "boolean", nullable: false),
                confirmed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                length = table.Column<TimeSpan>(type: "interval", nullable: false),
                segments = table.Column<string>(type: "jsonb", nullable: false),
                withdrawn_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_segment_answer", x => x.recording_id);
                table.CheckConstraint("ck_segment_answer_answers", "answers >= 1");
                table.CheckConstraint("ck_segment_answer_confirmed", "confirmed = (confirmed_at IS NOT NULL)");
                table.CheckConstraint("ck_segment_answer_segments", "jsonb_typeof(segments) = 'array' AND jsonb_array_length(segments) >= 1");
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_model_version_evaluation",
            table: "segment_model_version",
            sql: "(metrics IS NULL OR jsonb_typeof(metrics) = 'object')\nAND (stations IS NULL OR jsonb_typeof(stations) = 'array')\nAND (evidence IS NULL OR jsonb_typeof(evidence) = 'object')\nAND (rollback IS NULL OR jsonb_typeof(rollback) = 'object')\nAND (recommended_threshold IS NULL OR recommended_threshold BETWEEN 0 AND 1)");

        migrationBuilder.AddCheckConstraint(
            name: "ck_segment_catalog_entry_decided_by",
            table: "segment_catalog_entry",
            sql: "(decided_by IS NULL) = (kind = 'Unknown') AND (decided_by IS NULL OR decided_by IN ('Answer', 'Pattern'))");

        migrationBuilder.AddCheckConstraint(
            name: "ck_encode_job_chapters",
            table: "encode_job",
            sql: "((chapters_verdict IS NULL) = (chapters_decided_at IS NULL))\nAND ((chapters_verdict IS NULL) = (chapters_detector IS NULL))\nAND ((chapters_verdict IS NULL) = (chapters_break_share IS NULL))\nAND (chapters_verdict IS NULL OR status <> 'Queued')\nAND (chapters_verdict IS NULL OR chapters_verdict IN ('NotAsked', 'Marked', 'NothingFound', 'Discarded', 'Unreadable'))\nAND (chapters_detector IS NULL OR chapters_detector IN ('Nobody', 'Ffmpeg', 'Segments'))\nAND (chapters_break_share IS NULL OR chapters_break_share BETWEEN 0 AND 1)\nAND (chapters_decided_at IS NULL OR chapters_decided_at >= started_at)\nAND (chapters_model_version IS NULL OR chapters_model_version >= 1)\nAND ((chapters_model_version IS NULL AND chapters_answered_at IS NULL) OR chapters_detector IS NOT DISTINCT FROM 'Segments')\nAND (chapters_detector IS DISTINCT FROM 'Segments'\n    OR ((chapters_model_version IS NOT NULL OR chapters_answered_at IS NOT NULL) = (chapters_verdict <> 'NotAsked')))");

        migrationBuilder.CreateIndex(
            name: "ix_segment_answer_confirmed",
            table: "segment_answer",
            column: "confirmed_at",
            filter: "confirmed AND withdrawn_at IS NULL");
    }
}
