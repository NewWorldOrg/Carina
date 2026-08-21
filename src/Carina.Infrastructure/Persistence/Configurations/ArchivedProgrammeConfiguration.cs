using System.Text.Json;

using Carina.Domain.Channels;
using Carina.Domain.Programmes;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class ArchivedProgrammeConfiguration : IEntityTypeConfiguration<ArchivedProgramme>
{
    public void Configure(EntityTypeBuilder<ArchivedProgramme> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(
            "archived_programme",
            table => table.HasCheckConstraint(
                "ck_archived_programme_runs_forward",
                "end_at > start_at"));

        builder.HasKey(programme => new
        {
            programme.NetworkId,
            programme.ServiceId,
            programme.EventId,
            programme.StartsAt,
        });

        builder.Property(programme => programme.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id");

        builder.Property(programme => programme.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id");

        builder.Property(programme => programme.EventId)
            .HasConversion(id => id.Value, value => new EventId(value))
            .HasColumnName("event_id");

        builder.Property(programme => programme.StartsAt).HasColumnName("start_at");

        builder.Property(programme => programme.EndsAt).HasColumnName("end_at").IsRequired();

        builder.Property(programme => programme.Name)
            .HasColumnName("name")
            .HasMaxLength(Programme.NameMaxLength)
            .IsRequired();

        builder.Property(programme => programme.Summary)
            .HasColumnName("summary")
            .HasMaxLength(Programme.SummaryMaxLength)
            .IsRequired();

        builder.Property(programme => programme.HasSubtitles).HasColumnName("has_subtitles").IsRequired();

        builder.Property(programme => programme.Genres)
            .HasConversion(
                genres => JsonSerializer.Serialize(genres, ProgrammeJson.Options),
                stored => Read<ProgrammeGenre>(stored),
                Compared<ProgrammeGenre>())
            .HasColumnType("jsonb")
            .HasColumnName("genres")
            .IsRequired();

        builder.Property(programme => programme.Items)
            .HasConversion(
                items => JsonSerializer.Serialize(items, ProgrammeJson.Options),
                stored => Read<ProgrammeItem>(stored),
                Compared<ProgrammeItem>())
            .HasColumnType("jsonb")
            .HasColumnName("items")
            .IsRequired();

        builder.Property(programme => programme.ArchivedAt).HasColumnName("archived_at").IsRequired();

        builder.Property<string>(ProgrammeConfiguration.Searchable)
            .HasColumnName(ProgrammeConfiguration.Searchable)
            .HasComputedColumnSql(ProgrammeConfiguration.SearchableSql, stored: true);

        builder.Property<int[]>(ProgrammeConfiguration.GenreKinds)
            .HasColumnName(ProgrammeConfiguration.GenreKinds)
            .HasComputedColumnSql(ProgrammeConfiguration.GenreKindsSql, stored: true);

        builder.HasIndex(programme => programme.EndsAt);
        builder.HasIndex(programme => programme.StartsAt);
    }

    private static IReadOnlyList<T> Read<T>(string stored)
        => JsonSerializer.Deserialize<List<T>>(stored, ProgrammeJson.Options) ?? [];

    private static ValueComparer<IReadOnlyList<T>> Compared<T>()
        => new(
            (left, right) => left != null && right != null && left.SequenceEqual(right),
            list => list.Aggregate(0, (carried, item) => HashCode.Combine(carried, item)),
            list => list.ToList());
}
