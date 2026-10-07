using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class LearningDataBlockConfiguration : IEntityTypeConfiguration<LearningDataBlock>
{
    public const string TableName = "segment_learning_data";

    public void Configure(EntityTypeBuilder<LearningDataBlock> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_learning_data_kind", $"kind IN ({SegmentVocabulary.Numbers<LearningDataKind>()})");
            table.HasCheckConstraint("ck_segment_learning_data_chunk", $"chunk BETWEEN 0 AND {LearningData.LastChunk}");
            table.HasCheckConstraint("ck_segment_learning_data_bytes", "octet_length(bytes) > 0");
            table.HasCheckConstraint(
                "ck_segment_learning_data_version",
                $"version_number >= 1 AND version_origin IN ({SegmentVocabulary.Names<ExtractionOrigin>()})");
        });

        builder.HasKey(block => new { block.RecordingId, block.Kind, block.Chunk });

        builder.Property(block => block.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id");

        builder.Property(block => block.Kind).HasConversion<short>().HasColumnName("kind");
        builder.Property(block => block.Chunk).HasColumnName("chunk");
        builder.Property(block => block.Bytes).HasColumnName("bytes").HasColumnType("bytea").IsRequired();

        builder.ComplexProperty(block => block.Version, version =>
        {
            version.Property(detail => detail.Number).HasColumnName("version_number");
            version.Property(detail => detail.Origin)
                .HasConversion<string>()
                .HasColumnName("version_origin")
                .HasMaxLength(SegmentVocabulary.NameLength);
        });

        builder.Property(block => block.WrittenAt).HasColumnName("written_at").IsRequired();
    }
}
