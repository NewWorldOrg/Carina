using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class CatalogEntryConfiguration : IEntityTypeConfiguration<CatalogEntry>
{
    public const string TableName = "segment_catalog_entry";

    private const string Unknown = nameof(CatalogKind.Unknown);

    public void Configure(EntityTypeBuilder<CatalogEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_catalog_entry_kind", $"kind IN ({SegmentVocabulary.Names<CatalogKind>()})");
            table.HasCheckConstraint(
                "ck_segment_catalog_entry_confidence",
                $"(confidence IS NULL) = (kind = '{Unknown}') AND (confidence IS NULL OR confidence BETWEEN 0 AND 1)");
            table.HasCheckConstraint(
                "ck_segment_catalog_entry_decided_by",
                $"(decided_by IS NULL) = (kind = '{Unknown}') AND (decided_by IS NULL OR decided_by IN ({SegmentVocabulary.Names<CatalogDecision>()}))");
            table.HasCheckConstraint("ck_segment_catalog_entry_length", "length > interval '0'");
            table.HasCheckConstraint(
                "ck_segment_catalog_entry_fingerprint",
                $"octet_length(fingerprint) BETWEEN 4 AND {CatalogEntry.MostFrames * sizeof(uint)} AND octet_length(fingerprint) % 4 = 0");
            table.HasCheckConstraint("ck_segment_catalog_entry_counts", "occurrences >= 2 AND series_count >= 1 AND station_count >= 1");
            table.HasCheckConstraint("ck_segment_catalog_entry_seen", "last_seen_at >= first_seen_at");
            table.HasCheckConstraint("ck_segment_catalog_entry_kinds_version", $"kinds_version >= {CatalogKinds.Unsettled}");
        });

        builder.HasKey(entry => entry.Id);

        builder.Property(entry => entry.Id)
            .HasConversion(id => id.Value, value => new CatalogEntryId(value))
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(entry => entry.Kind)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .IsRequired();

        builder.Property(entry => entry.Confidence).HasColumnType("double precision");

        builder.Property(entry => entry.DecidedBy)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength);

        builder.Property(entry => entry.Length).HasColumnName("length").IsRequired();
        builder.Property(entry => entry.Fingerprint).HasColumnType("bytea").IsRequired();
        builder.Property(entry => entry.OccurrenceCount).HasColumnName("occurrences").IsRequired();
        builder.Property(entry => entry.SeriesCount).IsRequired();
        builder.Property(entry => entry.StationCount).IsRequired();
        builder.Property(entry => entry.FirstSeenAt).IsRequired();
        builder.Property(entry => entry.LastSeenAt).IsRequired();
        builder.Property(entry => entry.UpdatedAt).IsRequired();
        builder.Property(entry => entry.KindsVersion).HasDefaultValue(CatalogKinds.FirstVersion).IsRequired();
        builder.Ignore(entry => entry.AwaitsReestimation);

        builder.HasIndex(entry => entry.Kind);
        builder.HasIndex(entry => entry.LastSeenAt);
    }
}
