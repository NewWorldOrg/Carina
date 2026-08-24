namespace Carina.Infrastructure.Persistence.Configurations;

public static class RecordingGuards
{
    public const string Functions = """
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
        """;

    public const string Projection = """
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
        """;

    public const string Immutability = """
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
        """;
}
