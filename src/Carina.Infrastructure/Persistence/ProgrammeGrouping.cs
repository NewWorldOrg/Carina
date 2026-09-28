using Carina.Domain.Programmes;

namespace Carina.Infrastructure.Persistence;

/// <summary>
/// The statements that read the programmes a relayed or moved link joins.
/// </summary>
public static class ProgrammeGrouping
{
    public const string NamedParameter = "named";

    public static readonly string LinkingSql = $"""
        SELECT * FROM programme
        WHERE EXISTS (
            SELECT 1 FROM jsonb_array_elements(programme.related) AS link
            WHERE link ->> 'kind' IN ('{nameof(RelationKind.Relayed)}', '{nameof(RelationKind.Moved)}'))
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
