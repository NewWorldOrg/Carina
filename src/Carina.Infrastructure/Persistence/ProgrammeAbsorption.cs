using System.Text.Json;
using System.Text.Json.Serialization;

using Carina.Domain.Programmes;

using Carina.Infrastructure.Persistence.Configurations;

namespace Carina.Infrastructure.Persistence;

public static class ProgrammeAbsorption
{
    public const string RowsParameter = "rows";

    private const string End =
        "CASE WHEN COALESCE(excluded.end_at, programme.end_at) > excluded.start_at"
        + " THEN COALESCE(excluded.end_at, programme.end_at) END";

    private const string Name = "CASE WHEN excluded.name = '' THEN programme.name ELSE excluded.name END";

    private const string Summary = "CASE WHEN excluded.summary = '' THEN programme.summary ELSE excluded.summary END";

    private const string Genres = "CASE WHEN excluded.genres = '[]'::jsonb THEN programme.genres ELSE excluded.genres END";

    private const string Items = "CASE WHEN excluded.items = '[]'::jsonb THEN programme.items ELSE excluded.items END";

    private const string Related = "CASE WHEN excluded.related = '[]'::jsonb THEN programme.related ELSE excluded.related END";

    public static readonly string Sql = $"""
        WITH written AS (
            INSERT INTO programme (
                network_id, service_id, event_id, transport_stream_id, start_at, end_at, name, summary,
                is_shadow, genres, items, related, has_subtitles, source, updated_at)
            SELECT
                network_id, service_id, event_id, transport_stream_id, start_at, end_at, name, summary,
                is_shadow, genres, items, related, has_subtitles, source, updated_at
            FROM jsonb_to_recordset(@{RowsParameter}) AS arriving(
                network_id integer,
                service_id integer,
                event_id integer,
                transport_stream_id integer,
                start_at timestamptz,
                end_at timestamptz,
                name character varying({Programme.NameMaxLength}),
                summary character varying({Programme.SummaryMaxLength}),
                is_shadow boolean,
                genres jsonb,
                items jsonb,
                related jsonb,
                has_subtitles boolean,
                source character varying(32),
                updated_at timestamptz)
            ON CONFLICT (network_id, service_id, event_id) DO UPDATE SET
                transport_stream_id = excluded.transport_stream_id,
                start_at = excluded.start_at,
                end_at = {End},
                name = {Name},
                summary = {Summary},
                is_shadow = excluded.is_shadow,
                genres = {Genres},
                items = {Items},
                related = {Related},
                has_subtitles = excluded.has_subtitles,
                source = excluded.source,
                updated_at = excluded.updated_at,
                revision = nextval('{ProgrammeRevisions.Sequence}')
            WHERE (
                programme.transport_stream_id, programme.start_at, programme.end_at, programme.name, programme.summary,
                programme.is_shadow, programme.genres, programme.items, programme.related, programme.has_subtitles,
                programme.source)
            IS DISTINCT FROM (
                excluded.transport_stream_id, excluded.start_at, {End}, {Name}, {Summary},
                excluded.is_shadow, {Genres}, {Items}, {Related}, excluded.has_subtitles,
                excluded.source)
            RETURNING (xmax = 0) AS added)
        SELECT count(*) FILTER (WHERE added), count(*) FILTER (WHERE NOT added) FROM written
        """;

    public static string Rows(IEnumerable<Programme> programmes)
    {
        ArgumentNullException.ThrowIfNull(programmes);

        return JsonSerializer.Serialize(programmes.Select(Row.Of), ProgrammeJson.Options);
    }

    private sealed record Row(
        [property: JsonPropertyName("network_id")] int NetworkId,
        [property: JsonPropertyName("service_id")] int ServiceId,
        [property: JsonPropertyName("event_id")] int EventId,
        [property: JsonPropertyName("transport_stream_id")] int TransportStreamId,
        [property: JsonPropertyName("start_at")] DateTime StartAt,
        [property: JsonPropertyName("end_at")] DateTime? EndAt,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("is_shadow")] bool IsShadow,
        [property: JsonPropertyName("genres")] IReadOnlyList<ProgrammeGenre> Genres,
        [property: JsonPropertyName("items")] IReadOnlyList<ProgrammeItem> Items,
        [property: JsonPropertyName("related")] IReadOnlyList<RelatedProgramme> Related,
        [property: JsonPropertyName("has_subtitles")] bool HasSubtitles,
        [property: JsonPropertyName("source")] ProgrammeSource Source,
        [property: JsonPropertyName("updated_at")] DateTime UpdatedAt)
    {
        public static Row Of(Programme programme)
            => new(
                programme.NetworkId.Value,
                programme.ServiceId.Value,
                programme.EventId.Value,
                programme.TransportStreamId.Value,
                programme.StartsAt,
                programme.EndsAt,
                programme.Name,
                programme.Summary,
                programme.IsShadow,
                programme.Genres,
                programme.Items,
                programme.Related,
                programme.HasSubtitles,
                programme.Source,
                programme.UpdatedAt);
    }
}
