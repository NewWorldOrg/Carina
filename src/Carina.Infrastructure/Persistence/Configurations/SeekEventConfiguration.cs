using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class SeekEventConfiguration : IEntityTypeConfiguration<SeekEvent>
{
    public const string TableName = "segment_seek_event";

    public void Configure(EntityTypeBuilder<SeekEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_seek_event_event", $"event IN ({SegmentVocabulary.Names<SeekAction>()})");
            table.HasCheckConstraint("ck_segment_seek_event_means", $"means IN ({SegmentVocabulary.Names<SeekMeans>()})");
            table.HasCheckConstraint("ck_segment_seek_event_moments", "from_at >= interval '0' AND to_at >= interval '0'");
            table.HasCheckConstraint("ck_segment_seek_event_way", $"(event = '{SeekAction.Forward}') = (to_at > from_at) AND to_at <> from_at");
        });

        builder.HasKey(seek => seek.Id);

        builder.Property(seek => seek.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(seek => seek.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .IsRequired();

        builder.Property(seek => seek.HappenedAt).IsRequired();

        builder.Property(seek => seek.Event)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .HasColumnName("event")
            .IsRequired();

        builder.Property(seek => seek.Means)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .IsRequired();

        builder.Property(seek => seek.From).HasColumnName("from_at").IsRequired();
        builder.Property(seek => seek.To).HasColumnName("to_at").IsRequired();

        builder.HasIndex(seek => new { seek.RecordingId, seek.HappenedAt });
        builder.HasIndex(seek => seek.HappenedAt);
    }
}
