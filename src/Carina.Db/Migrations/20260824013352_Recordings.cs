using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Carina.Db.Migrations;

public partial class Recordings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION recording_json_count(entries jsonb) RETURNS integer
            LANGUAGE sql IMMUTABLE AS $fn$
            SELECT CASE WHEN jsonb_typeof(entries) = 'array' THEN jsonb_array_length(entries) ELSE 0 END;
            $fn$;

            CREATE OR REPLACE FUNCTION recording_history_holds(
                entries jsonb, resumes integer, faults text[], began timestamptz)
            RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
            SELECT jsonb_typeof(entries) = 'array'
               AND COALESCE(bool_and(
                       entry ->> 'fault' = ANY (faults)
                       AND entry ->> 'occurredAt' LIKE '%Z'
                       AND (entry ->> 'occurredAt')::timestamptz >= began
                       AND (entry ->> 'resumedAt' IS NULL OR entry ->> 'resumedAt' LIKE '%Z')
                       AND (entry ->> 'resumedAt' IS NULL
                            OR (entry ->> 'resumedAt')::timestamptz >= (entry ->> 'occurredAt')::timestamptz)
                       AND (previous IS NULL OR (entry ->> 'occurredAt')::timestamptz >= previous)
                       AND (entry ->> 'resumedAt' IS NOT NULL OR position = last_position)
                   ), true)
               AND COALESCE(count(*) FILTER (WHERE entry ->> 'resumedAt' IS NOT NULL), 0) = resumes
            FROM (
                SELECT value AS entry,
                       ordinality AS position,
                       count(*) OVER () AS last_position,
                       lag(COALESCE((value ->> 'resumedAt')::timestamptz, (value ->> 'occurredAt')::timestamptz))
                           OVER (ORDER BY ordinality) AS previous
                FROM jsonb_array_elements(
                         CASE WHEN jsonb_typeof(entries) = 'array' THEN entries ELSE '[]'::jsonb END)
                     WITH ORDINALITY AS listed(value, ordinality)
            ) AS ordered;
            $fn$;

            CREATE OR REPLACE FUNCTION recording_reasons_hold(
                entries jsonb, faults text[], tune_failures text[], began timestamptz)
            RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
            SELECT jsonb_typeof(entries) = 'array'
               AND COALESCE(bool_and(
                       entry ->> 'fault' = ANY (faults)
                       AND entry ->> 'noticedAt' LIKE '%Z'
                       AND (entry ->> 'noticedAt')::timestamptz >= began
                       AND (entry ->> 'tuneFailure' IS NULL OR entry ->> 'tuneFailure' = ANY (tune_failures))
                   ), true)
            FROM jsonb_array_elements(
                     CASE WHEN jsonb_typeof(entries) = 'array' THEN entries ELSE '[]'::jsonb END) AS listed(entry);
            $fn$;

            CREATE OR REPLACE FUNCTION recording_positions_hold(positions jsonb, dropped bigint, scrambled bigint)
            RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
            SELECT jsonb_typeof(positions) = 'array'
               AND COALESCE(bool_and(
                       (bucket ->> 'second')::int >= 0
                       AND (previous IS NULL OR (bucket ->> 'second')::int > previous)
                       AND (bucket ->> 'continuity')::bigint >= 0
                       AND (bucket ->> 'scrambled')::bigint >= 0
                       AND ((bucket ->> 'continuity')::bigint > 0 OR (bucket ->> 'scrambled')::bigint > 0)
                   ), true)
               AND COALESCE(sum((bucket ->> 'continuity')::bigint), 0) <= COALESCE(dropped, 0)
               AND COALESCE(sum((bucket ->> 'scrambled')::bigint), 0) <= COALESCE(scrambled, 0)
            FROM (
                SELECT value AS bucket,
                       lag((value ->> 'second')::int) OVER (ORDER BY ordinality) AS previous
                FROM jsonb_array_elements(
                         CASE WHEN jsonb_typeof(positions) = 'array' THEN positions ELSE '[]'::jsonb END)
                     WITH ORDINALITY AS listed(value, ordinality)
            ) AS ordered;
            $fn$;

            CREATE OR REPLACE FUNCTION recording_reanchors_hold(entries jsonb, wraps_at bigint)
            RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
            SELECT jsonb_typeof(entries) = 'array'
               AND COALESCE(bool_and(
                       (entry ->> 'second')::int >= 0
                       AND (previous IS NULL OR (entry ->> 'second')::int > previous)
                       AND (entry ->> 'before')::bigint BETWEEN 0 AND wraps_at - 1
                       AND (entry ->> 'after')::bigint BETWEEN 0 AND wraps_at - 1
                   ), true)
            FROM (
                SELECT value AS entry,
                       lag((value ->> 'second')::int) OVER (ORDER BY ordinality) AS previous
                FROM jsonb_array_elements(
                         CASE WHEN jsonb_typeof(entries) = 'array' THEN entries ELSE '[]'::jsonb END)
                     WITH ORDINALITY AS listed(value, ordinality)
            ) AS ordered;
            $fn$;

            CREATE OR REPLACE FUNCTION recording_reasons_name_any(entries jsonb, faults text[])
            RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
            SELECT COALESCE(bool_or(entry ->> 'fault' = ANY (faults)), false)
            FROM jsonb_array_elements(
                     CASE WHEN jsonb_typeof(entries) = 'array' THEN entries ELSE '[]'::jsonb END) AS listed(entry);
            $fn$;
            """);

        migrationBuilder.CreateTable(
            name: "recording",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                reservation_id = table.Column<Guid>(type: "uuid", nullable: true),
                network_id = table.Column<int>(type: "integer", nullable: false),
                service_id = table.Column<int>(type: "integer", nullable: false),
                event_id = table.Column<int>(type: "integer", nullable: false),
                programme_start_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                output_root = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                file_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                file_size_observed = table.Column<long>(type: "bigint", nullable: true),
                observed_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                started_at_actual = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                stopped_at_actual = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                aborted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                written_duration_ms = table.Column<long>(type: "bigint", nullable: false),
                resume_count = table.Column<int>(type: "integer", nullable: false),
                interruptions = table.Column<string>(type: "jsonb", nullable: false),
                expected_window_start = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                expected_window_end = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                recording_outcome = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                outcome_detail = table.Column<string>(type: "jsonb", nullable: false),
                cc_measured = table.Column<bool>(type: "boolean", nullable: false),
                cc_dropped_packets = table.Column<long>(type: "bigint", nullable: true),
                cc_total_packets = table.Column<long>(type: "bigint", nullable: true),
                scrambled_packets = table.Column<long>(type: "bigint", nullable: true),
                eovf_count = table.Column<long>(type: "bigint", nullable: false),
                tuner_device_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                thumbnail_state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                measured_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                snapshot_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                snapshot_summary = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                snapshot_extended = table.Column<string>(type: "character varying(65536)", maxLength: 65536, nullable: false),
                snapshot_genres = table.Column<string>(type: "jsonb", nullable: false),
                captured_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                broadcast_group_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                broadcast_group_role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                pcr_anchor = table.Column<long>(type: "bigint", nullable: true),
                drop_positions = table.Column<string>(type: "jsonb", nullable: false),
                pcr_reanchors = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_recording", x => x.id);
                table.CheckConstraint("ck_recording_broadcast_group", "broadcast_group_role IN ('Standalone', 'MovementPrimary', 'MovementSuppressed', 'RelaySegment')\nAND (broadcast_group_role = 'Standalone' OR broadcast_group_key IS NOT NULL)");
                table.CheckConstraint("ck_recording_complete_was_asked_for", "recording_outcome IS DISTINCT FROM 'Complete' OR aborted_at IS NOT NULL");
                table.CheckConstraint("ck_recording_counts", "written_duration_ms >= 0\nAND resume_count >= 0\nAND eovf_count >= 0\nAND (file_size_observed IS NULL OR file_size_observed >= 0)\nAND (scrambled_packets IS NULL OR scrambled_packets >= 0)");
                table.CheckConstraint("ck_recording_drop_positions", "(pcr_anchor IS NOT NULL\n    OR (recording_json_count(drop_positions) = 0 AND recording_json_count(pcr_reanchors) = 0))\nAND (pcr_anchor IS NULL OR cc_measured)\nAND (pcr_anchor IS NULL OR pcr_anchor BETWEEN 0 AND 8589934591)");
                table.CheckConstraint("ck_recording_empty_file_failed", "recording_outcome IS NULL OR file_size_observed <> 0 OR recording_outcome = 'Failed'");
                table.CheckConstraint("ck_recording_file_name", "btrim(file_name) = file_name\nAND length(file_name) > 0\nAND file_name <> '.'\nAND strpos(file_name, '/') = 0\nAND strpos(file_name, chr(92)) = 0\nAND strpos(file_name, '..') = 0\nAND strpos(file_name, replace(id::text, '-', '')) > 0");
                table.CheckConstraint("ck_recording_history", "recording_history_holds(interruptions, resume_count, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow']::text[], started_at_actual)");
                table.CheckConstraint("ck_recording_measurement", "(cc_measured\n    OR (cc_dropped_packets IS NULL AND cc_total_packets IS NULL))\nAND (NOT cc_measured\n    OR (cc_dropped_packets IS NOT NULL\n        AND cc_total_packets IS NOT NULL\n        AND cc_dropped_packets <= cc_total_packets\n        AND measured_updated_at IS NOT NULL))");
                table.CheckConstraint("ck_recording_observation", "(file_size_observed IS NULL) = (observed_at IS NULL)");
                table.CheckConstraint("ck_recording_outcome", "(recording_outcome IS NULL OR recording_outcome IN ('Complete', 'Truncated', 'Failed'))\nAND (recording_outcome IS NULL OR stopped_at_actual IS NOT NULL)\nAND (recording_outcome IS NULL OR file_size_observed IS NOT NULL)");
                table.CheckConstraint("ck_recording_outcome_detail", "recording_outcome IS NULL\nOR recording_outcome = 'Complete'\nOR recording_json_count(outcome_detail) > 0");
                table.CheckConstraint("ck_recording_positions", "recording_positions_hold(drop_positions, cc_dropped_packets, scrambled_packets)");
                table.CheckConstraint("ck_recording_reanchors", "recording_reanchors_hold(pcr_reanchors, 8589934592)");
                table.CheckConstraint("ck_recording_reasons", "recording_reasons_hold(outcome_detail, ARRAY['TuneFailed', 'RefusedByDiskPrecheck', 'DiskExhausted', 'DriverLost', 'DrainGraceExpired', 'StoppedByHand', 'TunerContended', 'ScramblingUnresolved', 'ShortOfTheWindow']::text[], ARRAY['NoLock', 'NoData', 'IncompletePsi', 'StreamMismatch']::text[], started_at_actual)");
                table.CheckConstraint("ck_recording_runs_forwards", "(stopped_at_actual IS NULL OR stopped_at_actual >= started_at_actual)\nAND (aborted_at IS NULL OR aborted_at >= started_at_actual)\nAND (observed_at IS NULL OR observed_at >= started_at_actual)\nAND (measured_updated_at IS NULL OR measured_updated_at >= started_at_actual)");
                table.CheckConstraint("ck_recording_thumbnail", "thumbnail_state IN ('Pending', 'Ready', 'Failed', 'Skipped')\nAND (recording_outcome IS DISTINCT FROM 'Failed' OR thumbnail_state <> 'Ready')");
                table.CheckConstraint("ck_recording_tuner", "tuner_device_id IS NOT NULL\nOR (NOT cc_measured\n    AND eovf_count = 0\n    AND NOT recording_reasons_name_any(outcome_detail, ARRAY['TuneFailed', 'DriverLost', 'TunerContended', 'ScramblingUnresolved']::text[]))");
                table.CheckConstraint("ck_recording_window", "expected_window_end > expected_window_start");
            });

        migrationBuilder.CreateIndex(
            name: "ix_recording_cc_dropped",
            table: "recording",
            column: "cc_dropped_packets",
            filter: "cc_measured AND cc_dropped_packets > 0");

        migrationBuilder.CreateIndex(
            name: "ix_recording_in_flight",
            table: "recording",
            column: "started_at_actual",
            filter: "recording_outcome IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_recording_settled",
            table: "recording",
            columns: new[] { "recording_outcome", "stopped_at_actual" });

        migrationBuilder.Sql(
            """
            CREATE OR REPLACE FUNCTION recording_projects_its_outcome() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF NEW.reservation_id IS NOT NULL THEN
                    UPDATE reservation
                    SET recording_outcome = NEW.recording_outcome
                    WHERE id = NEW.reservation_id
                      AND recording_outcome IS DISTINCT FROM NEW.recording_outcome;
                END IF;

                RETURN NULL;
            END;
            $fn$;

            CREATE TRIGGER recording_projects_its_outcome
            AFTER INSERT OR UPDATE OF recording_outcome, reservation_id ON recording
            FOR EACH ROW EXECUTE FUNCTION recording_projects_its_outcome();

            CREATE OR REPLACE FUNCTION recording_keeps_its_reservation() RETURNS trigger
            LANGUAGE plpgsql AS $fn$
            BEGIN
                IF OLD.reservation_id IS DISTINCT FROM NEW.reservation_id THEN
                    RAISE EXCEPTION 'a recording keeps the reservation it was started for'
                        USING ERRCODE = '23514', CONSTRAINT = 'ck_recording_keeps_its_reservation';
                END IF;

                RETURN NEW;
            END;
            $fn$;

            CREATE TRIGGER recording_keeps_its_reservation
            BEFORE UPDATE OF reservation_id ON recording
            FOR EACH ROW EXECUTE FUNCTION recording_keeps_its_reservation();
            """);

        migrationBuilder.CreateIndex(
            name: "ux_recording_file",
            table: "recording",
            columns: new[] { "output_root", "file_name" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_recording_reservation",
            table: "recording",
            column: "reservation_id",
            unique: true,
            filter: "reservation_id IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        ArgumentNullException.ThrowIfNull(migrationBuilder);

        migrationBuilder.DropTable(
            name: "recording");

        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_projects_its_outcome();");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_keeps_its_reservation();");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_json_count(jsonb);");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_history_holds(jsonb, integer, text[], timestamptz);");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_reasons_hold(jsonb, text[], text[], timestamptz);");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_positions_hold(jsonb, bigint, bigint);");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_reanchors_hold(jsonb, bigint);");
        migrationBuilder.Sql("DROP FUNCTION IF EXISTS recording_reasons_name_any(jsonb, text[]);");
    }
}
