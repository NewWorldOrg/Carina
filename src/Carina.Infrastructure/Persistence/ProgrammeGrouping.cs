using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Persistence;

public static class ProgrammeGrouping
{
    public const string NamedParameter = "named";

    public static readonly string LinkingSql = $"""
        SELECT * FROM programme
        WHERE EXISTS (
            SELECT 1 FROM jsonb_array_elements(programme.related) AS link
            WHERE link ->> 'kind' IN ('{nameof(RelationKind.Relayed)}', '{nameof(RelationKind.Moved)}'))
        """;

    public static readonly string SimulcastSql = $"""
        WITH named AS (
            SELECT network_id, service_id, event_id, transport_stream_id, start_at, end_at, name, related
            FROM programme
            WHERE NOT is_shadow),
        shared AS (
            SELECT one.network_id, one.service_id, one.event_id,
                   other.network_id AS other_network_id,
                   other.service_id AS other_service_id,
                   other.event_id AS other_event_id
            FROM named AS one
            CROSS JOIN LATERAL jsonb_array_elements(one.related) AS link
            JOIN named AS other
              ON other.network_id = (link ->> 'networkId')::integer
             AND other.service_id = (link ->> 'serviceId')::integer
             AND other.event_id = (link ->> 'eventId')::integer
            WHERE link ->> 'kind' = '{nameof(RelationKind.Shared)}'
              AND other.service_id <> one.service_id),
        alike AS (
            SELECT one.network_id, one.service_id, one.event_id
            FROM named AS one
            JOIN named AS other
              ON other.network_id = one.network_id
             AND other.transport_stream_id = one.transport_stream_id
             AND other.start_at = one.start_at
             AND other.end_at = one.end_at
             AND other.name = one.name
             AND other.service_id <> one.service_id
            WHERE one.name <> ''),
        simulcast AS (
            SELECT network_id, service_id, event_id FROM shared
            UNION SELECT other_network_id, other_service_id, other_event_id FROM shared
            UNION SELECT network_id, service_id, event_id FROM alike)
        SELECT programme.* FROM programme
        JOIN simulcast USING (network_id, service_id, event_id)
        """;

    public static readonly string NamedSql = $"""
        SELECT programme.* FROM programme
        JOIN jsonb_to_recordset(@{NamedParameter}) AS named(
            network_id integer,
            service_id integer,
            event_id integer)
        USING (network_id, service_id, event_id)
        """;
}
