namespace Carina.Infrastructure.Persistence.Configurations;

public static class RecordingGuards
{
    public const string Functions = """
        CREATE OR REPLACE FUNCTION recording_json_count(entries jsonb) RETURNS integer
        LANGUAGE sql IMMUTABLE AS $fn$
        SELECT CASE WHEN jsonb_typeof(entries) = 'array' THEN jsonb_array_length(entries) ELSE 0 END;
        $fn$;

        CREATE OR REPLACE FUNCTION recording_utc_instant(value text) RETURNS timestamptz
        LANGUAGE sql IMMUTABLE AS $fn$
        SELECT CASE WHEN value ~ '^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?Z$'
                    THEN value::timestamptz
               END;
        $fn$;

        CREATE OR REPLACE FUNCTION recording_whole_number(value jsonb, lowest numeric, highest numeric)
        RETURNS numeric LANGUAGE sql IMMUTABLE AS $fn$
        SELECT CASE WHEN jsonb_typeof(value) = 'number'
                     AND (value #>> '{}')::numeric = trunc((value #>> '{}')::numeric)
                     AND (value #>> '{}')::numeric BETWEEN lowest AND highest
                    THEN (value #>> '{}')::numeric
               END;
        $fn$;

        CREATE OR REPLACE FUNCTION recording_history_holds(
            entries jsonb, resumes integer, faults text[], began timestamptz)
        RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
        SELECT jsonb_typeof(entries) = 'array'
           AND COALESCE(bool_and(COALESCE(
                   entry ? 'fault' AND entry ? 'occurredAt' AND entry ? 'resumedAt'
                   AND entry ->> 'fault' = ANY (faults)
                   AND occurred IS NOT NULL
                   AND occurred >= began
                   AND (entry ->> 'resumedAt' IS NULL) = (resumed IS NULL)
                   AND (resumed IS NULL OR resumed >= occurred)
                   AND (previous IS NULL OR occurred >= previous)
                   AND (resumed IS NOT NULL OR position = last_position),
                   false)), true)
           AND COALESCE(count(*) FILTER (WHERE entry ->> 'resumedAt' IS NOT NULL), 0) = resumes
        FROM (
            SELECT value AS entry,
                   ordinality AS position,
                   count(*) OVER () AS last_position,
                   recording_utc_instant(value ->> 'occurredAt') AS occurred,
                   recording_utc_instant(value ->> 'resumedAt') AS resumed,
                   lag(COALESCE(recording_utc_instant(value ->> 'resumedAt'),
                                recording_utc_instant(value ->> 'occurredAt')))
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
           AND COALESCE(bool_and(COALESCE(
                   entry ? 'fault' AND entry ? 'tuneFailure' AND entry ? 'note' AND entry ? 'noticedAt'
                   AND entry ->> 'fault' = ANY (faults)
                   AND jsonb_typeof(entry -> 'note') = 'string'
                   AND noticed IS NOT NULL
                   AND noticed >= began
                   AND (entry ->> 'tuneFailure' IS NULL OR entry ->> 'tuneFailure' = ANY (tune_failures)),
                   false)), true)
        FROM (
            SELECT value AS entry,
                   recording_utc_instant(value ->> 'noticedAt') AS noticed
            FROM jsonb_array_elements(
                     CASE WHEN jsonb_typeof(entries) = 'array' THEN entries ELSE '[]'::jsonb END) AS listed(value)
        ) AS listed;
        $fn$;

        CREATE OR REPLACE FUNCTION recording_positions_hold(positions jsonb, dropped bigint, scrambled bigint)
        RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
        SELECT jsonb_typeof(positions) = 'array'
           AND COALESCE(bool_and(
                   at_second IS NOT NULL AND lost IS NOT NULL AND unresolved IS NOT NULL
                   AND (previous IS NULL OR at_second > previous)
                   AND (lost > 0 OR unresolved > 0)), true)
           AND COALESCE(sum(lost), 0) <= COALESCE(dropped, 0)
           AND COALESCE(sum(unresolved), 0) <= COALESCE(scrambled, 0)
        FROM (
            SELECT value AS bucket,
                   recording_whole_number(value -> 'second', 0, 2147483647) AS at_second,
                   recording_whole_number(value -> 'continuity', 0, 9223372036854775807) AS lost,
                   recording_whole_number(value -> 'scrambled', 0, 9223372036854775807) AS unresolved,
                   lag(recording_whole_number(value -> 'second', 0, 2147483647))
                       OVER (ORDER BY ordinality) AS previous
            FROM jsonb_array_elements(
                     CASE WHEN jsonb_typeof(positions) = 'array' THEN positions ELSE '[]'::jsonb END)
                 WITH ORDINALITY AS listed(value, ordinality)
        ) AS ordered;
        $fn$;

        CREATE OR REPLACE FUNCTION recording_reanchors_hold(entries jsonb, wraps_at bigint)
        RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
        SELECT jsonb_typeof(entries) = 'array'
           AND COALESCE(bool_and(
                   at_second IS NOT NULL AND was IS NOT NULL AND became IS NOT NULL
                   AND (previous IS NULL OR at_second > previous)), true)
        FROM (
            SELECT value AS entry,
                   recording_whole_number(value -> 'second', 0, 2147483647) AS at_second,
                   recording_whole_number(value -> 'before', 0, wraps_at - 1) AS was,
                   recording_whole_number(value -> 'after', 0, wraps_at - 1) AS became,
                   lag(recording_whole_number(value -> 'second', 0, 2147483647))
                       OVER (ORDER BY ordinality) AS previous
            FROM jsonb_array_elements(
                     CASE WHEN jsonb_typeof(entries) = 'array' THEN entries ELSE '[]'::jsonb END)
                 WITH ORDINALITY AS listed(value, ordinality)
        ) AS ordered;
        $fn$;

        CREATE OR REPLACE FUNCTION recording_reasons_name_any(entries jsonb, faults text[])
        RETURNS boolean LANGUAGE sql IMMUTABLE AS $fn$
        SELECT COALESCE(bool_or(COALESCE(entry ->> 'fault' = ANY (faults), false)), false)
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
        AFTER INSERT OR UPDATE OF recording_outcome ON recording
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
