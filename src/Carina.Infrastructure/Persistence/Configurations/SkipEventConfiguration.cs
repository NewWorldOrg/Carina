using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class SkipEventConfiguration : IEntityTypeConfiguration<SkipEvent>
{
    public const string TableName = "segment_skip_event";

    public void Configure(EntityTypeBuilder<SkipEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_skip_event_event", $"event IN ({SegmentVocabulary.Names<ViewerActionKind>()})");
            table.HasCheckConstraint("ck_segment_skip_event_kind", $"kind IN ({string.Join(", ", SkipEvent.KindsOffered.Select(kind => $"'{kind}'"))})");
            table.HasCheckConstraint("ck_segment_skip_event_moments", "from_at >= interval '0' AND to_at >= interval '0'");
        });

        builder.HasKey(skip => skip.Id);

        builder.Property(skip => skip.Id)
            .HasColumnName("id")
            .UseIdentityByDefaultColumn();

        builder.Property(skip => skip.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .IsRequired();

        builder.Property(skip => skip.HappenedAt).IsRequired();

        builder.Property(skip => skip.Event)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .HasColumnName("event")
            .IsRequired();

        builder.Property(skip => skip.Kind)
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength)
            .IsRequired();

        builder.Property(skip => skip.From).HasColumnName("from_at").IsRequired();
        builder.Property(skip => skip.To).HasColumnName("to_at").IsRequired();
        builder.Property(skip => skip.ModelVersion).HasColumnName("model_version");

        builder.HasOne<SegmentModelVersion>()
            .WithMany()
            .HasForeignKey(skip => skip.ModelVersion)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(skip => new { skip.RecordingId, skip.HappenedAt });
        builder.HasIndex(skip => skip.HappenedAt);
    }
}
