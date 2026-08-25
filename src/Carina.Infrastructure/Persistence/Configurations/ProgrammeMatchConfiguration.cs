using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ProgrammeMatchConfiguration : IEntityTypeConfiguration<ProgrammeMatch>
{
    public void Configure(EntityTypeBuilder<ProgrammeMatch> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasNoKey();
        builder.ToSqlQuery(BothLayers);

        builder.Property(match => match.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(match => match.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(match => match.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(match => match.StartsAt).HasColumnName("start_at");
        builder.Property(match => match.EndsAt).HasColumnName("end_at");
        builder.Property(match => match.Name).HasColumnName("name");
        builder.Property(match => match.Summary).HasColumnName("summary");
        builder.Property(match => match.IsShadow).HasColumnName("is_shadow");
        builder.Property(match => match.HasSubtitles).HasColumnName("has_subtitles");
        builder.Property(match => match.Source).HasConversion<string>().HasColumnName("source");
        builder.Property(match => match.Revision).HasColumnName("revision");
        builder.Property(match => match.IsArchived).HasColumnName("is_archived");

        builder.Property(match => match.Genres)
            .HasConversion(
                genres => JsonSerializer.Serialize(genres, ProgrammeJson.Options),
                stored => Read<ProgrammeGenre>(stored),
                Compared<ProgrammeGenre>())
            .HasColumnType("jsonb")
            .HasColumnName("genres");

        builder.Property(match => match.Items)
            .HasConversion(
                items => JsonSerializer.Serialize(items, ProgrammeJson.Options),
                stored => Read<ProgrammeItem>(stored),
                Compared<ProgrammeItem>())
            .HasColumnType("jsonb")
            .HasColumnName("items");

        builder.Property(match => match.Related)
            .HasConversion(
                related => JsonSerializer.Serialize(related, ProgrammeJson.Options),
                stored => Read<RelatedProgramme>(stored),
                Compared<RelatedProgramme>())
            .HasColumnType("jsonb")
            .HasColumnName("related");

        builder.Property<string>(ProgrammeConfiguration.Searchable)
            .HasColumnName(ProgrammeConfiguration.Searchable);

        builder.Property<int[]>(ProgrammeConfiguration.GenreKinds)
            .HasColumnName(ProgrammeConfiguration.GenreKinds);
    }

    public const string BothLayers = """
        SELECT
            layered.network_id,
            layered.service_id,
            layered.event_id,
            layered.start_at,
            layered.end_at,
            layered.name,
            layered.summary,
            layered.is_shadow,
            layered.has_subtitles,
            layered.source,
            layered.revision,
            layered.genres,
            layered.items,
            layered.related,
            layered.searchable,
            layered.genre_kinds,
            layered.is_archived
        FROM (
            SELECT
                network_id,
                service_id,
                event_id,
                start_at,
                end_at,
                name,
                summary,
                is_shadow,
                has_subtitles,
                source,
                revision,
                genres,
                items,
                related,
                searchable,
                genre_kinds,
                false AS is_archived
            FROM programme
            UNION ALL
            SELECT
                kept.network_id,
                kept.service_id,
                kept.event_id,
                kept.start_at,
                kept.end_at,
                kept.name,
                kept.summary,
                false,
                kept.has_subtitles,
                NULL::character varying(32),
                NULL::bigint,
                kept.genres,
                kept.items,
                '[]'::jsonb,
                kept.searchable,
                kept.genre_kinds,
                true
            FROM archived_programme AS kept
        ) AS layered
        WHERE NOT EXISTS (
            SELECT 1
            FROM programme AS held
            WHERE layered.is_archived
              AND held.network_id = layered.network_id
              AND held.service_id = layered.service_id
              AND held.event_id = layered.event_id
              AND held.start_at = layered.start_at)
        """;

    private static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
