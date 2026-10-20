using Carina.Domain.Segments;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Carina.Infrastructure.Persistence.Configurations;

/// <summary>
/// The rounds of learning taken, one row per day, so that a round already taken is not taken again after the
/// process starts again, each with the candidate it kept, so that a round knows what is new since the last one
/// that learned, and with what became of it, the verdict on its candidate and the figures of the version in use
/// it was judged against, so that the settings screen can say why the version in use did or did not change. Rows
/// kept before the last three were kept leave them empty.
/// </summary>
public sealed class SegmentLearningRoundConfiguration : IEntityTypeConfiguration<SegmentLearningRound>
{
    public const string TableName = "segment_learning_round";

    private const string Stopped = nameof(SegmentTrainingOutcome.Stopped);

    public void Configure(EntityTypeBuilder<SegmentLearningRound> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable(TableName, table =>
        {
            table.HasCheckConstraint(
                "ck_segment_learning_round_outcome",
                $"outcome IN ({SegmentVocabulary.Names<SegmentTrainingOutcome>()}) AND outcome <> '{Stopped}'");
            table.HasCheckConstraint("ck_segment_learning_round_verdict", $"verdict IN ({SegmentVocabulary.Names<AdoptionVerdict>()})");
            table.HasCheckConstraint(
                "ck_segment_learning_round_judged",
                "(candidate IS NOT NULL OR (verdict IS NULL AND in_use_metrics IS NULL)) AND (in_use_metrics IS NULL OR jsonb_typeof(in_use_metrics) = 'object')");
        });

        builder.HasKey(round => round.Day);

        builder.Property(round => round.Day)
            .HasColumnName("day")
            .HasColumnType("date")
            .ValueGeneratedNever();

        builder.Property(round => round.TakenAt).IsRequired();

        builder.Property(round => round.Candidate).HasColumnName("candidate");

        builder.Property(round => round.Outcome)
            .HasColumnName("outcome")
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength);

        builder.Property(round => round.Verdict)
            .HasColumnName("verdict")
            .HasConversion<string>()
            .HasMaxLength(SegmentVocabulary.NameLength);

        builder.Property(round => round.InUseMetrics)
            .HasConversion(metrics => SegmentJson.Written(metrics!), stored => SegmentJson.Metrics(stored), SegmentJson.ComparedAsWritten<SegmentMetrics>(SegmentJson.Written))
            .HasColumnName("in_use_metrics")
            .HasColumnType("jsonb");
    }
}
