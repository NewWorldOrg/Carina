using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ProgrammeConfiguration : IEntityTypeConfiguration<Programme>
{
    public void Configure(EntityTypeBuilder<Programme> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "programme",
            table =>
            {
                table.HasCheckConstraint("ck_programme_runs_forward", "end_at IS NULL OR end_at > start_at");
                table.HasCheckConstraint(
                    "ck_programme_source",
                    "source IN ('PresentFollowing', 'ScheduleBasic', 'ScheduleExtended')");
            });

        builder.HasKey(programme => new { programme.NetworkId, programme.ServiceId, programme.EventId });

        builder.Ignore(programme => programme.Id);

        builder.Property(programme => programme.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(programme => programme.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(programme => programme.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(programme => programme.TransportStreamId)
            .HasConversion(id => id.Value, value => new TransportStreamId(value))
            .HasColumnName("transport_stream_id")
            .IsRequired();

        builder.Property(programme => programme.StartsAt).HasColumnName("start_at").IsRequired();
        builder.Property(programme => programme.EndsAt).HasColumnName("end_at");

        builder.Property(programme => programme.Name)
            .HasMaxLength(Programme.NameMaxLength)
            .IsRequired();

        builder.Property(programme => programme.Summary)
            .HasMaxLength(Programme.SummaryMaxLength)
            .IsRequired();

        builder.Property(programme => programme.IsShadow).IsRequired();
        builder.Property(programme => programme.HasSubtitles).IsRequired();

        builder.Property(programme => programme.Source)
            .HasConversion<string>()
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(programme => programme.Genres)
            .HasConversion(
                genres => JsonSerializer.Serialize(genres, ProgrammeJson.Options),
                stored => Read<ProgrammeGenre>(stored),
                Compared<ProgrammeGenre>())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(programme => programme.Items)
            .HasConversion(
                items => JsonSerializer.Serialize(items, ProgrammeJson.Options),
                stored => Read<ProgrammeItem>(stored),
                Compared<ProgrammeItem>())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(programme => programme.Related)
            .HasConversion(
                related => JsonSerializer.Serialize(related, ProgrammeJson.Options),
                stored => Read<RelatedProgramme>(stored),
                Compared<RelatedProgramme>())
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(programme => programme.UpdatedAt).HasColumnName("updated_at").IsRequired();

        builder.Property(programme => programme.Revision)
            .HasColumnName("revision")
            .HasDefaultValueSql($"nextval('{ProgrammeRevisions.Sequence}')")
            .IsRequired();

        builder.Property<string>(Searchable)
            .HasColumnName(Searchable)
            .HasComputedColumnSql("lower(name || ' ' || summary)", stored: true);

        builder.Property<int[]>(GenreKinds)
            .HasColumnName(GenreKinds)
            .HasComputedColumnSql(GenreKindsSql, stored: true);

        builder.HasIndex(programme => programme.Revision).IsUnique();

        builder.HasIndex(programme => programme.StartsAt);
        builder.HasIndex(programme => programme.UpdatedAt);
    }

    public const string Searchable = "searchable";

    public const string GenreKinds = "genre_kinds";

    public const string GenreKindsSql =
        "string_to_array("
        + "nullif(translate(jsonb_path_query_array(genres, '$[*].kind')::text, '[] ', ''), '')"
        + ", ',')::integer[]";

    private static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
