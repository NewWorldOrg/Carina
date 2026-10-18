using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class RecordingDetectionConfiguration : IEntityTypeConfiguration<RecordingDetection>
{
    public const string TableName = "segment_detection";

    private const string Marked = nameof(SegmentDetectionState.Marked);

    public void Configure(EntityTypeBuilder<RecordingDetection> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_detection_state", $"state IN ({SegmentVocabulary.Names<SegmentDetectionState>()})");
            table.HasCheckConstraint(
                "ck_segment_detection_segments",
                $"""
                jsonb_typeof(segments) = 'array'
                AND jsonb_array_length(segments) >= 1
                AND (state <> '{Marked}' OR jsonb_array_length(segments) >= 2)
                AND jsonb_typeof(snaps) = 'array'
                """);
            table.HasCheckConstraint("ck_segment_detection_cm_share", "cm_share BETWEEN 0 AND 1");
            table.HasCheckConstraint("ck_segment_detection_extraction_version", "extraction_version >= 1");
        });

        builder.HasKey(detection => new { detection.RecordingId, detection.ModelVersion });

        builder.Property(detection => detection.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .ValueGeneratedNever();

        builder.Property(detection => detection.ModelVersion)
            .HasColumnName("model_version")
            .ValueGeneratedNever();

        builder.Property(detection => detection.State)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .IsRequired();

        builder.Property(detection => detection.Segments)
            .HasConversion(
                segments => SegmentJson.Written(segments),
                stored => SegmentJson.Segments(stored),
                SegmentJson.ComparedAsWritten<IReadOnlyList<SegmentSpan>>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(detection => detection.CmShare).HasColumnType("double precision").IsRequired();

        builder.Property(detection => detection.Snaps)
            .HasConversion(
                snaps => SegmentJson.Written(snaps),
                stored => SegmentJson.Seconds(stored),
                SegmentJson.ComparedAsWritten<IReadOnlyList<TimeSpan>>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(detection => detection.ExtractionVersionNumber).HasColumnName("extraction_version").IsRequired();
        builder.Property(detection => detection.CatalogUntil).IsRequired();
        builder.Property(detection => detection.DetectedAt).IsRequired();

        builder.HasOne<SegmentModelVersion>()
            .WithMany()
            .HasForeignKey(detection => detection.ModelVersion)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(detection => detection.ModelVersion);
        builder.HasIndex(detection => detection.DetectedAt);
    }
}
