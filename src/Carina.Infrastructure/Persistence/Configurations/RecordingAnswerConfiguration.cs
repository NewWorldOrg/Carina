using Carina.Domain.Recordings;
using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

public sealed class RecordingAnswerConfiguration : IEntityTypeConfiguration<RecordingAnswer>
{
    public const string TableName = "segment_answer";

    public const string ConfirmedIndexName = "ix_segment_answer_confirmed";

    public void Configure(EntityTypeBuilder<RecordingAnswer> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint("ck_segment_answer_confirmed", "confirmed = (confirmed_at IS NOT NULL)");
            table.HasCheckConstraint("ck_segment_answer_answers", "answers >= 1");
            table.HasCheckConstraint("ck_segment_answer_segments", "jsonb_typeof(segments) = 'array' AND jsonb_array_length(segments) >= 1");
        });

        builder.HasKey(answer => answer.RecordingId);

        builder.Property(answer => answer.RecordingId)
            .HasConversion(id => id.Value, value => new RecordingId(value))
            .HasColumnName("recording_id")
            .ValueGeneratedNever();

        builder.Property(answer => answer.Segments)
            .HasConversion(
                segments => SegmentJson.Written(segments),
                stored => SegmentJson.Segments(stored),
                SegmentJson.ComparedAsWritten<IReadOnlyList<SegmentSpan>>(SegmentJson.Written))
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(answer => answer.Length).IsRequired();
        builder.Property(answer => answer.Confirmed).IsRequired();
        builder.Property(answer => answer.ConfirmedAt);
        builder.Property(answer => answer.AnsweredAt).IsRequired();
        builder.Property(answer => answer.Answers).IsRequired();
        builder.Property(answer => answer.WithdrawnAt);

        builder.Ignore(answer => answer.Stands);
        builder.Ignore(answer => answer.BreakShare);

        builder.HasIndex(answer => answer.ConfirmedAt)
            .HasFilter("confirmed AND withdrawn_at IS NULL")
            .HasDatabaseName(ConfirmedIndexName);
    }
}
