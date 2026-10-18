using System.Globalization;

using Carina.Domain.Channels;
using Carina.Domain.Recordings;
using Carina.Domain.Reservations;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class CatalogOccurrenceConfiguration : IEntityTypeConfiguration<CatalogOccurrence>
{
    public const string TableName = "segment_catalog_occurrence";

    public const string PlaceIndexName = "ux_segment_catalog_occurrence_place";

    public void Configure(EntityTypeBuilder<CatalogOccurrence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_catalog_occurrence_span", "starts_at >= interval '0' AND ends_at > starts_at");
            table.HasCheckConstraint(
                "ck_segment_catalog_occurrence_bit_error_rate",
                $"bit_error_rate BETWEEN 0 AND {SegmentMatcher.MostBitErrors.ToString(CultureInfo.InvariantCulture)}::real");
            table.HasCheckConstraint(
                "ck_segment_catalog_occurrence_kind_override",
                $"kind_override IS NULL OR kind_override IN ({SegmentVocabulary.Names<SegmentKind>()})");
        });

        builder.HasKey(occurrence => occurrence.Id);

        builder.Property(occurrence => occurrence.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(occurrence => occurrence.EntryId)
            .HasConversion(id => id.Value, value => new CatalogEntryId(value))
            .HasColumnName("entry_id")
            .IsRequired();

        builder.Property(occurrence => occurrence.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .IsRequired();

        builder.Property(occurrence => occurrence.StartsAt).IsRequired();
        builder.Property(occurrence => occurrence.EndsAt).IsRequired();

        builder.Property(occurrence => occurrence.NetworkId)
            .HasConversion(id => id.Value, value => new NetworkId(value))
            .HasColumnName("network_id")
            .IsRequired();

        builder.Property(occurrence => occurrence.ServiceId)
            .HasConversion(id => id.Value, value => new ServiceId(value))
            .HasColumnName("service_id")
            .IsRequired();

        builder.Property(occurrence => occurrence.SeriesName).HasMaxLength(Reservation.NameMaxLength);
        builder.Property(occurrence => occurrence.RecordingStartedAt).IsRequired();
        builder.Property(occurrence => occurrence.BitErrorRate).HasColumnType("real").IsRequired();

        builder.Property(occurrence => occurrence.KindOverride)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength);

        builder.Property(occurrence => occurrence.MatchedAt).IsRequired();

        builder.Ignore(occurrence => occurrence.Length);
        builder.Ignore(occurrence => occurrence.Span);
        builder.Ignore(occurrence => occurrence.Member);

        builder.HasOne<CatalogEntry>()
            .WithMany()
            .HasForeignKey(occurrence => occurrence.EntryId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(occurrence => new { occurrence.EntryId, occurrence.RecordingId, occurrence.StartsAt })
            .IsUnique()
            .HasDatabaseName(PlaceIndexName);

        builder.HasIndex(occurrence => occurrence.RecordingId);
        builder.HasIndex(occurrence => occurrence.RecordingStartedAt);
    }
}
