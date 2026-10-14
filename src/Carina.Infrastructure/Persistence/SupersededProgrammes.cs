using System.Globalization;

using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Persistence;

/// <summary>
/// <see cref="ProgrammeSupersession"/> written in SQL, for every read that shows the guide. Two programmes of one
/// service share airtime only where, in start order, one begins before the latest end so far or ends after the
/// next one begins, so a single ordered pass over the service's programmes finds them, and the heard marks are
/// compared among those alone rather than across the whole service. The archive is judged when programmes move
/// into it, and its reads filter on the verdict kept beside each row.
/// </summary>
public static class SupersededProgrammes
{
    private static readonly string End = string.Create(
        CultureInfo.InvariantCulture,
        $"COALESCE(end_at, start_at + INTERVAL '{ProgrammeSupersession.LengthWhenTheEndIsNotAnnounced.TotalSeconds} seconds')");

    /// <summary>
    /// Every programme a programme heard later has superseded, each named by one number: its network, service and
    /// event identifiers side by side, sixteen bits apiece, so a check against it compares one integer per
    /// programme rather than three columns.
    /// </summary>
    public static readonly string Sql = $"""
        WITH ordered AS MATERIALIZED (
            SELECT
                network_id,
                service_id,
                event_id,
                start_at,
                {End} AS ends,
                max({End}) OVER earlier AS earlier_end,
                lead(start_at) OVER earlier AS next_start
            FROM programme
            WHERE NOT is_shadow
            WINDOW earlier AS (
                PARTITION BY network_id, service_id
                ORDER BY start_at, event_id
                ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING)),
        sharing AS (
            SELECT
                ordered.network_id,
                ordered.service_id,
                ordered.event_id,
                ordered.start_at,
                ordered.ends,
                (SELECT heard.last_heard_at
                 FROM programme AS heard
                 WHERE heard.network_id = ordered.network_id
                   AND heard.service_id = ordered.service_id
                   AND heard.event_id = ordered.event_id) AS last_heard_at
            FROM ordered
            WHERE ordered.earlier_end > ordered.start_at OR ordered.next_start < ordered.ends)
        SELECT {Key("older")}
        FROM sharing AS older
        WHERE EXISTS (
            SELECT 1
            FROM sharing AS newer
            WHERE newer.network_id = older.network_id
              AND newer.service_id = older.service_id
              AND newer.last_heard_at > COALESCE(older.last_heard_at, '-infinity'::timestamptz)
              AND newer.start_at < older.ends
              AND newer.ends > older.start_at)
        """;

    /// <summary>
    /// Every programme of the guide but the superseded ones, to compose a query on.
    /// </summary>
    public static readonly string StandingSql = $"""
        SELECT programme.*
        FROM programme
        WHERE {StandsIn("programme")}
        """;

    /// <summary>
    /// Holds for a row of <paramref name="table"/> that no programme heard later has superseded. The superseded
    /// programmes are gathered into an array once, before the scan, so this is a filter rather than a join: a
    /// union arm it is pushed into stays a plain scan and can still be read in parallel.
    /// </summary>
    public static string StandsIn(string table)
        => $"{Key(table)} <> ALL (ARRAY({Sql}))";

    private static string Key(string table)
        => $"(({table}.network_id::bigint << 32) | ({table}.service_id::bigint << 16) | {table}.event_id)";
}
